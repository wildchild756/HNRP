// <copyright file="ClusterCullingLightPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 簇剔除光源 pass：用 compute shader 把可见光按屏幕簇裁剪，
    /// 输出每个簇的光照掩码缓冲，供前向渲染 pass 使用。
    /// </summary>
    [Pass(PassNameConst)]
    public sealed class ClusterCullingLightPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。
        /// </summary>
        public const string PassNameConst = "Cluster Culling Light";

        // ── Slot ──

        /// <summary>
        /// 光照数据缓冲输入 slot。连接到 <see cref="BuildLightDataPass"/> 的输出。
        /// </summary>
        public ComputeBufferSlot LightDatasBufferSlot { get; private set; }

        /// <summary>
        /// 簇剔除光照掩码缓冲输出 slot。
        /// 连接到前向渲染 pass 的光照掩码输入。
        /// </summary>
        public ComputeBufferSlot ClusterCullingLightMaskBufferSlot { get; private set; }

        // ── 相机上下文 ──

        private CameraContext cameraContext;

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="ClusterCullingLightPass"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// </remarks>
        public ClusterCullingLightPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ClusterCullingLightPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public ClusterCullingLightPass(string passName)
            : base(passName)
        {
        }

        // ── 生命周期 ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            LightDatasBufferSlot = new ComputeBufferSlot(
                "lightDatasBuffer", SlotDirection.Input);
            RegisterSlot(LightDatasBufferSlot);
            ClusterCullingLightMaskBufferSlot = new ComputeBufferSlot(
                "clusterCullingLightMaskBuffer", SlotDirection.Output);
            RegisterSlot(ClusterCullingLightMaskBufferSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 保存相机上下文，使 <see cref="Record"/> 期间能访问 compute shader 与相机
        /// 数据。compute shader 从 <see cref="CameraContext.RuntimeResources"/> 解析。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 从已连接的输入 slot 读取光照数据缓冲，创建簇剔除光照掩码输出缓冲，
        /// 配置并派发簇剔除 compute shader，随后发布输出句柄。
        ///
        /// compute shader 与相机矩阵来自 <see cref="PreRecord"/> 设置的相机上下文。
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            if (ClusterCullingLightMaskBufferSlot == null
                || LightDatasBufferSlot == null)
            {
                IsEnabled &= false;
            }

            if (cameraContext == null)
            {
                IsEnabled &= false;
            }

            ComputeShader clusterCullingLightCS =
                cameraContext.RuntimeResources?.clusterCullingLightCS;
            if (clusterCullingLightCS == null)
            {
                Debug.LogError(
                    "Cluster Culling Light Compute Shader 为 null。 " +
                    "请确保已在 HNRenderPipelineRuntimeResources 中赋值。");
                IsEnabled &= false;
            }

            Camera camera = cameraContext.Camera;
            if (camera == null)
            {
                IsEnabled &= false;
            }

            using (var builder = renderGraph.AddRenderPass<ClusterCullingLightPassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                // ── 输入：光照数据缓冲 ──

                if (LightDatasBufferSlot?.IsConnected == true)
                {
                    passData.lightDatasBuffer = builder.ReadComputeBuffer(
                        LightDatasBufferSlot.ReadHandle());
                }

                // ── 输出：簇剔除光照掩码缓冲 ──

                ComputeBufferHandle lightMaskBuffer = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MAX_CLUSTER_MASK_WORDS,
                        sizeof(uint))
                    { name = "Cluster Culling Light Mask Buffer" });

                passData.clusterCullingLightMaskBuffer = builder.WriteComputeBuffer(lightMaskBuffer);

                ClusterCullingLightMaskBufferSlot.SetHandle(lightMaskBuffer);

                // ── 准备每帧数据 ──

                int maxLightOnScreen =
                    HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                    + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;
                int catchedLightCount = Mathf.Min(
                    cameraContext.VisibleLights.Length, maxLightOnScreen);

                int directionalLightCount = 0;
                int localLightCount = 0;
                for (int i = 0; i < catchedLightCount; i++)
                {
                    var light = cameraContext.VisibleLights[i];
                    if (light.lightType == LightType.Directional)
                    {
                        directionalLightCount++;
                    }

                    if (light.lightType == LightType.Point
                        || light.lightType == LightType.Spot)
                    {
                        localLightCount++;
                    }
                }

                if (directionalLightCount > 0)
                {
                    directionalLightCount -= 1;
                }

                int mainLightIndex = GetMainLightIndex(cameraContext.VisibleLights);

                int2 screenResolution =
                    math.int2(camera.pixelWidth, camera.pixelHeight);
                int3 clusterSize = GetClusterSize(screenResolution);
                int clusterCount =
                    clusterSize.x * clusterSize.y * clusterSize.z;
                float2 clusterZScaleOffset = GetClusterZScaleOffset(
                    clusterSize,
                    camera.orthographic,
                    camera.nearClipPlane,
                    camera.farClipPlane);

                // 每簇条目数 = 屏幕上可见光源总数
                int itemsPerCluster = maxLightOnScreen;
                int wordsPerCluster =
                    (itemsPerCluster + 31) / 32 + 1 /* 1 表示 header */;

                // ── 配置 pass data ──

                passData.clusterCullingLightCS = clusterCullingLightCS;
                passData.clusterCullingLightKernel =
                    clusterCullingLightCS.FindKernel(
                        CLUSTER_CULLING_CS_KERNEL_NAME);

                passData.clusterCullingLightParams.clusterSize =
                    new Vector2(clusterSize.x, clusterSize.y);
                passData.clusterCullingLightParams.clusterZScaleOffset =
                    new Vector2(clusterZScaleOffset.x, clusterZScaleOffset.y);
                passData.clusterCullingLightParams.wordsPerCluster =
                    wordsPerCluster;
                passData.clusterCullingLightParams.directionalLightCount =
                    directionalLightCount;
                passData.clusterCullingLightParams.localLightCount =
                    localLightCount;
                passData.clusterCullingLightParams.mainLightIndex = 
                    mainLightIndex;

                // 相机矩阵
                Matrix4x4 clipToView = camera.projectionMatrix;
                Matrix4x4 viewToClip = camera.projectionMatrix.inverse;
                Matrix4x4 clipToWorld =
                    (camera.worldToCameraMatrix * camera.projectionMatrix)
                    .inverse;

                // ── 把每帧值存到池化 pass data 上，
                // 使渲染函数闭包只捕获 `this`（零分配）──

                passData.clusterSize = clusterSize;
                passData.clusterZScaleOffset = clusterZScaleOffset;
                passData.wordsPerCluster = wordsPerCluster;
                passData.catchedLightCount = catchedLightCount;
                passData.cameraOrthographic = camera.orthographic;
                passData.clipToView = clipToView;
                passData.viewToClip = viewToClip;
                passData.clipToWorld = clipToWorld;

                // ── 渲染函数 ──

                builder.SetRenderFunc(
                    (ClusterCullingLightPassData data, RenderGraphContext ctx) =>
                    {
                        if (!IsEnabled)
                        {
                            return;
                        }

                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            PropertyIDs.clusterCullingLightMaskBuffer,
                            data.clusterCullingLightMaskBuffer);
                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            BuildLightDataPass.PropertyIDs.LightDatasBuffer,
                            data.lightDatasBuffer);

                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingParams0,
                            new Vector4(
                                data.clusterZScaleOffset.x,
                                data.clusterZScaleOffset.y,
                                data.wordsPerCluster,
                                data.cameraOrthographic ? 1.0f : 0.0f));
                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingParams1,
                            new Vector4(
                                data.clusterSize.x,
                                data.clusterSize.y,
                                data.clusterSize.z,
                                data.catchedLightCount));

                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingClipToViewMatrix,
                            data.clipToView);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingViewToClipMatrix,
                            data.viewToClip);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingClipToWorldMatrix,
                            data.clipToWorld);

                        int threadGroup = (clusterCount + 63) / 64;
                        int threadGroupY =
                            (threadGroup + data.clusterSize.y - 1) / data.clusterSize.y;

                        ctx.cmd.DispatchCompute(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            data.clusterSize.y,
                            threadGroupY,
                            1);

                        ConstantBuffer.PushGlobal(
                            ctx.cmd,
                            data.clusterCullingLightParams,
                            PropertyIDs.clusterCullingLightParamsBuffer);
                    });
            }
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // 本 pass 不持有可释放资源。
        }

        // ── 辅助 ──

        /// <summary>
        /// 基于屏幕分辨率计算当前帧的簇网格尺寸。
        /// </summary>
        /// <param name="screenResolution">以像素为单位的屏幕分辨率。</param>
        /// <returns>簇在 X、Y、Z 三个方向上的尺寸。</returns>
        private static int3 GetClusterSize(int2 screenResolution)
        {
            int2 clusterSizeXY = new int2(1, 1);
            int sliceCount = CLUSTER_MIN_Z_SLIZE;
            int tileWidth = 8 >> 1;
            do
            {
                tileWidth <<= 1;
                clusterSizeXY = (screenResolution + tileWidth - 1) / Mathf.Max(1, tileWidth - 1);
                int tileCountPerSlice = clusterSizeXY.x * clusterSizeXY.y;
                sliceCount = MAX_CLUSTER_MASK_WORDS / Mathf.Max(1, tileCountPerSlice - 1);
            }
            while (sliceCount < CLUSTER_MIN_Z_SLIZE
                   || sliceCount > CLUSTER_MAX_Z_SLICE);
            return new int3(clusterSizeXY.x, clusterSizeXY.y, sliceCount);
        }

        /// <summary>
        /// 计算簇网格的 Z 轴缩放与偏移，
        /// 正交相机与透视相机使用不同公式。
        /// </summary>
        /// <param name="clusterSize">簇网格尺寸。</param>
        /// <param name="isOrthographic">相机是否为正交投影。</param>
        /// <param name="nearClipPlane">相机近裁剪面。</param>
        /// <param name="farClipPlane">相机远裁剪面。</param>
        /// <returns>
        /// 包含 Z 轴缩放（x）与偏移（y）的 <see cref="float2"/>。
        /// </returns>
        private static float2 GetClusterZScaleOffset(
            int3 clusterSize,
            bool isOrthographic,
            float nearClipPlane,
            float farClipPlane)
        {
            float2 scaleOffset = new float2(0, 0);
            if (isOrthographic)
            {
                scaleOffset.x =
                    (float)clusterSize.z / (farClipPlane - nearClipPlane);
                scaleOffset.y = -nearClipPlane * scaleOffset.x;
            }
            else
            {
                scaleOffset.x =
                    (float)clusterSize.z
                    / (math.log2(farClipPlane) - math.log2(nearClipPlane));
                scaleOffset.y = -math.log2(nearClipPlane) * scaleOffset.x;
            }

            return scaleOffset;
        }

        private static int GetMainLightIndex(NativeArray<VisibleLight> visibleLights)
        {
            int visibleLightsCount = visibleLights.Length;
            if(visibleLightsCount == 0)
                return -1;

            Light sunLight = RenderSettings.sun;
            int brightestDirectionalLightIndex = -1;
            float brightestLightIntensity = 0.0f;
            for(int i = 0; i < visibleLightsCount; i++)
            {
                VisibleLight currVisibleLight = visibleLights[i];
                Light currLight = currVisibleLight.light;

                // 粒子系统light的light property为null，粒子系统light会排列在visibleLights中的末尾
                // 因此，如果第一个light是粒子系统light，那么后续所有light都是粒子系统light
                // 此时我们要么已获取到main light，要么当前visibleLights中没有main light
                if(currLight == null)
                    break;

                if(currVisibleLight.lightType == LightType.Directional)
                {
                    // sun light如果设置则选择sun light
                    if(currLight == sunLight)
                        return i;
                    
                    // sun light没有设置则选最亮的directional light
                    if(currLight.intensity > brightestLightIntensity)
                    {
                        brightestLightIntensity = currLight.intensity;
                        brightestDirectionalLightIndex = i;
                    }
                }
            }

            return brightestDirectionalLightIndex;
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="ClusterCullingLightPass"/> 的渲染图 pass 数据容器。
        /// </summary>
        private sealed class ClusterCullingLightPassData
        {
            /// <summary>
            /// 光照数据计算缓冲句柄（来自 <see cref="BuildLightDataPass"/> 的输入）。
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// 簇剔除光照掩码缓冲句柄（输出）。
            /// </summary>
            public ComputeBufferHandle clusterCullingLightMaskBuffer;

            /// <summary>
            /// 簇剔除 compute shader。
            /// </summary>
            public ComputeShader clusterCullingLightCS;

            /// <summary>
            /// 簇剔除派发使用的 kernel 索引。
            /// </summary>
            public int clusterCullingLightKernel;

            /// <summary>
            /// 簇剔除光照的全局常量缓冲参数。
            /// </summary>
            public ClusterCullingLightParams clusterCullingLightParams;

            /// <summary>
            /// 本帧簇网格尺寸。
            /// </summary>
            public int3 clusterSize;

            /// <summary>
            /// 簇深度切片的 Z 轴缩放（x）与偏移（y）。
            /// </summary>
            public float2 clusterZScaleOffset;

            /// <summary>
            /// 每个簇掩码的 uint 字数。
            /// </summary>
            public int wordsPerCluster;

            /// <summary>
            /// 本帧参与剔除的光源数。
            /// </summary>
            public int catchedLightCount;

            /// <summary>
            /// 当前相机是否为正交投影。
            /// </summary>
            public bool cameraOrthographic;

            /// <summary>
            /// 裁剪空间到视图空间矩阵。
            /// </summary>
            public Matrix4x4 clipToView;

            /// <summary>
            /// 视图空间到裁剪空间矩阵。
            /// </summary>
            public Matrix4x4 viewToClip;

            /// <summary>
            /// 裁剪空间到世界空间矩阵。
            /// </summary>
            public Matrix4x4 clipToWorld;
        }

        // ── 常量 ──

        private const int MAX_CLUSTER_MASK_WORDS = 4096 * 4;
        private const int CLUSTER_MIN_Z_SLIZE = 16;
        private const int CLUSTER_MAX_Z_SLICE = 128;
        private const string CLUSTER_CULLING_CS_KERNEL_NAME =
            "ClusterCullingLightCS";

        // ── 数据结构 ──

        /// <summary>
        /// 簇剔除光照参数的 GPU 侧常量缓冲布局。
        /// 必须与 compute shader 中声明的布局一致。
        /// </summary>
        public unsafe struct ClusterCullingLightParams
        {
            /// <summary>簇网格在 X 与 Y 方向上的尺寸。</summary>
            public Vector2 clusterSize;

            /// <summary>
            /// 簇深度切片的 Z 轴缩放（x）与偏移（y）。
            /// </summary>
            public Vector2 clusterZScaleOffset;

            /// <summary>每个簇掩码的 uint 字数。</summary>
            public int wordsPerCluster;

            /// <summary>方向光数量（不含主光）。</summary>
            public int directionalLightCount;

            /// <summary>点光与聚光灯数量。</summary>
            public int localLightCount;

            /// <summary>为保持 16 字节对齐而填充。</summary>
            public int mainLightIndex;
        }

        /// <summary>
        /// 本 pass 与其消费方使用的 shader 属性标识。
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// 簇剔除光照掩码缓冲的 shader 属性 ID。值：<c>_ClusterCullingLightMaskBuffer</c>。
            /// </summary>
            public static readonly int clusterCullingLightMaskBuffer =
                Shader.PropertyToID("_ClusterCullingLightMaskBuffer");

            /// <summary>
            /// 簇剔除光照参数常量缓冲的 shader 属性 ID。
            /// 值：<c>_ClusterCullingLightParamsBuffer</c>。
            /// </summary>
            public static readonly int clusterCullingLightParamsBuffer =
                Shader.PropertyToID("_ClusterCullingLightParamsBuffer");

            /// <summary>
            /// culling 参数 0 的 shader 属性 ID（zScale、zOffset、
            /// wordsPerCluster、isOrthographic）。值：<c>_ClusterCullingLightParams0</c>。
            /// </summary>
            public static readonly int cullingParams0 =
                Shader.PropertyToID("_ClusterCullingLightParams0");

            /// <summary>
            /// culling 参数 1 的 shader 属性 ID（clusterSizeX、clusterSizeY、
            /// clusterSizeZ、visibleLightCount）。值：<c>_ClusterCullingLightParams1</c>。
            /// </summary>
            public static readonly int cullingParams1 =
                Shader.PropertyToID("_ClusterCullingLightParams1");

            /// <summary>
            /// 裁剪空间到视图空间矩阵的 shader 属性 ID。
            /// 值：<c>_ClusterCullingLightClipToView</c>。
            /// </summary>
            public static readonly int cullingClipToViewMatrix =
                Shader.PropertyToID("_ClusterCullingLightClipToView");

            /// <summary>
            /// 视图空间到裁剪空间矩阵的 shader 属性 ID。
            /// 值：<c>_ClusterCullingLightViewToClip</c>。
            /// </summary>
            public static readonly int cullingViewToClipMatrix =
                Shader.PropertyToID("_ClusterCullingLightViewToClip");

            /// <summary>
            /// 裁剪空间到世界空间矩阵的 shader 属性 ID。
            /// 值：<c>_ClusterCullingLightClipToWorld</c>。
            /// </summary>
            public static readonly int cullingClipToWorldMatrix =
                Shader.PropertyToID("_ClusterCullingLightClipToWorld");
        }
    }
}
