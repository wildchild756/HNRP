// <copyright file="DrawObjectPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 通用参数化对象绘制 pass。
    /// </summary>
    [Pass(PassNameConst)]
    public sealed class DrawObjectPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。
        /// </summary>
        public const string PassNameConst = "Draw Object";

        // ── 可配置参数 ──

        /// <summary>
        /// 获取或设置颜色目标分配参数。
        /// </summary>
        public TextureResourceParams ColorTargetParams
        {
            get => colorTargetParams;
            set => colorTargetParams = value;
        }

        /// <summary>
        /// 获取或设置深度目标分配参数。
        /// </summary>
        public TextureResourceParams DepthTargetParams
        {
            get => depthTargetParams;
            set => depthTargetParams = value;
        }

        /// <summary>
        /// 获取或设置渲染器列表分配参数。
        /// </summary>
        public RendererListParams RendererListParams
        {
            get => rendererListParams;
            set => rendererListParams = value;
        }

        /// <summary>
        /// 获取或设置本地分配渲染器列表（经 <see cref="RendererListParams"/>）
        /// 时使用的渲染层掩码。默认为 <c>0x00000001</c>（第 0 层）。
        /// </summary>
        public uint RenderingLayerMask
        {
            get => rendererListParams.RenderingLayerMask;
            set => rendererListParams.RenderingLayerMask = value;
        }

        /// <summary>
        /// 获取或设置物体是否接收阴影。默认为 <c>true</c>。
        /// </summary>
        public bool ReceiveShadow
        {
            get => receiveShadow;
            set => receiveShadow = value;
        }

        // ── Slot ──

        /// <summary>
        /// 颜色目标输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetSlot { get; private set; }

        /// <summary>
        /// 深度目标输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot DepthTargetSlot { get; private set; }

        /// <summary>
        /// 级联阴影图输入 slot。
        /// </summary>
        public TextureSlot CascadeShadowMapSlot { get; private set; }

        /// <summary>
        /// 屏幕空间阴影图输入 slot。
        /// </summary>
        public TextureSlot ScreenSpaceShadowMapSlot { get; private set; }

        /// <summary>
        /// 光照数据计算缓冲输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public ComputeBufferSlot LightDatasSlot { get; private set; }

        /// <summary>
        /// 反射探针图集纹理输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ReflectionProbeAtlasSlot { get; private set; }

        /// <summary>
        /// 簇剔除反射探针掩码缓冲输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public ComputeBufferSlot ProbeMaskSlot { get; private set; }

        /// <summary>
        /// 簇剔除反射探针数据缓冲输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public ComputeBufferSlot ProbeDatasSlot { get; private set; }

        /// <summary>
        /// 簇剔除光源掩码缓冲输入 slot。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public ComputeBufferSlot LightMaskSlot { get; private set; }

        /// <summary>
        /// 颜色目标输出 slot（解析后 <see cref="ColorTargetSlot"/> 句柄的透传，
        /// 供下游链式连接）。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetOutputSlot { get; private set; }

        /// <summary>
        /// 深度目标输出 slot（解析后 <see cref="DepthTargetSlot"/> 句柄的透传，
        /// 供下游链式连接）。<see cref="Pass.SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot DepthTargetOutputSlot { get; private set; }

        /// <summary>
        /// <see cref="ColorTargetSlot"/> 输入未连接 / 无效时本地分配
        /// 颜色目标所用的参数。默认：全分辨率 LDR。
        /// </summary>
        [SerializeField]
        private TextureResourceParams colorTargetParams;

        /// <summary>
        /// <see cref="DepthTargetSlot"/> 输入未连接 / 无效时本地分配
        /// 深度目标所用的参数。默认：全分辨率 32 位深度。
        /// </summary>
        [SerializeField]
        private TextureResourceParams depthTargetParams;

        /// <summary>
        /// 本地创建渲染器列表所用的参数。默认：不透明队列、层掩码
        /// <c>0x00000001</c>。
        /// </summary>
        [SerializeField]
        private RendererListParams rendererListParams;

        /// <summary>
        /// 物体是否接收阴影。
        /// </summary>
        [SerializeField]
        private bool receiveShadow = true;

        // ── 相机上下文 ──

        private CameraContext cameraContext;

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="DrawObjectPass"/> 的新实例。
        /// pass 以文档化默认分配参数启动（全分辨率 LDR 颜色、32 位深度、不透明
        /// 渲染器列表）；<see cref="PreRecord"/> 之后按当前图的设置推导每帧目标格式。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// 参数默认值与带名构造保持一致。
        /// </remarks>
        public DrawObjectPass()
            : base(string.Empty)
        {
            colorTargetParams = TextureResourceParams.CreateDefault();
            depthTargetParams = TextureResourceParams.CreateDefault();
            depthTargetParams.DepthBits = DepthBits.Depth32;
            rendererListParams = RendererListParams.CreateDefault();
        }

        /// <summary>
        /// 初始化 <see cref="DrawObjectPass"/> 的新实例。
        /// pass 以文档化默认分配参数启动（全分辨率 LDR 颜色、32 位深度、不透明
        /// 渲染器列表）；<see cref="PreRecord"/> 之后按当前图的设置推导每帧目标格式。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public DrawObjectPass(string passName)
            : base(passName)
        {
            colorTargetParams = TextureResourceParams.CreateDefault();
            depthTargetParams = TextureResourceParams.CreateDefault();
            depthTargetParams.DepthBits = DepthBits.Depth32;
            rendererListParams = RendererListParams.CreateDefault();
        }

        // ── 生命周期 ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            ColorTargetSlot = new TextureSlot("ColorTarget", SlotDirection.Input);
            RegisterSlot(ColorTargetSlot);
            DepthTargetSlot = new TextureSlot("DepthTarget", SlotDirection.Input);
            RegisterSlot(DepthTargetSlot);
            CascadeShadowMapSlot = new TextureSlot("ShadowMap", SlotDirection.Input);
            RegisterSlot(CascadeShadowMapSlot);
            LightDatasSlot = new ComputeBufferSlot("LightDatas", SlotDirection.Input);
            RegisterSlot(LightDatasSlot);
            ReflectionProbeAtlasSlot = new TextureSlot("ReflectionProbeAtlas", SlotDirection.Input);
            RegisterSlot(ReflectionProbeAtlasSlot);
            ProbeMaskSlot = new ComputeBufferSlot("ProbeMask", SlotDirection.Input);
            RegisterSlot(ProbeMaskSlot);
            ProbeDatasSlot = new ComputeBufferSlot("ProbeDatas", SlotDirection.Input);
            RegisterSlot(ProbeDatasSlot);
            LightMaskSlot = new ComputeBufferSlot("LightMask", SlotDirection.Input);
            RegisterSlot(LightMaskSlot);

            ColorTargetOutputSlot = new TextureSlot("ColorTargetOutput", SlotDirection.Output);
            RegisterSlot(ColorTargetOutputSlot);
            DepthTargetOutputSlot = new TextureSlot("DepthTargetOutput", SlotDirection.Output);
            RegisterSlot(DepthTargetOutputSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 保存相机上下文，使 <see cref="Record"/> 期间能解析渲染器列表 /
        /// 光照全局参数，并按图的设置确定颜色格式。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;

            // 仅按模板级设置派生颜色格式：颜色链头自建颜色目标时，其格式由
            // template.Settings.AllowHDR 决定。不再重置其它可配置参数
            // （深度参数、渲染器列表、阴影开关等）——默认值已在构造函数初始化，
            // 逐帧重置会覆盖模板代码 / 编辑器缓存 / 运行时动态设置的参数。
            colorTargetParams.ColorFormat = template.Settings.AllowHDR
                ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)
                : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
        }

        /// <inheritdoc />
        public override void Record(RenderGraph renderGraph)
        {
            if (ColorTargetSlot == null || DepthTargetSlot == null || cameraContext == null)
            {
                IsEnabled = false;
                return;
            }

            Camera camera = cameraContext.Camera;
            if (camera == null)
            {
                IsEnabled = false;
                return;
            }

            // ── 必需输入：颜色 / 深度目标 + 渲染器列表 ──
            // 输入已连接且句柄有效时消费连接输入；否则按本 pass 参数本地分配资源。

            bool useInputColor = ColorTargetSlot.IsConnected && ColorTargetSlot.HasHandle;
            TextureHandle colorTarget = useInputColor
                ? ColorTargetSlot.ReadHandle()
                : renderGraph.CreateTexture(
                    colorTargetParams.CreateDesc("Color Buffer", camera));

            bool useInputDepth = DepthTargetSlot.IsConnected && DepthTargetSlot.HasHandle;
            TextureHandle depthTarget = useInputDepth
                ? DepthTargetSlot.ReadHandle()
                : renderGraph.CreateTexture(
                    depthTargetParams.CreateDesc("Depth Buffer", camera));

            RendererListHandle rendererList = CreateRendererList(renderGraph);

            if (!colorTarget.IsValid() || !depthTarget.IsValid() || !rendererList.IsValid())
            {
                IsEnabled = false;
                return;
            }

            // 把解析出的颜色 / 深度句柄透传到输出 slot，
            // 使下游 pass 能从本 pass 输出继续链式连接。
            if (ColorTargetOutputSlot != null)
            {
                ColorTargetOutputSlot.SetHandle(colorTarget);
            }

            if (DepthTargetOutputSlot != null)
            {
                DepthTargetOutputSlot.SetHandle(depthTarget);
            }

            using var builder = renderGraph.AddRenderPass<DrawObjectPassData>(
                PassName, out var passData);

            builder.AllowRendererListCulling(false);

            passData.colorTarget = builder.UseColorBuffer(colorTarget, 0);
            passData.depthTarget = builder.UseDepthBuffer(depthTarget, DepthAccess.ReadWrite);

            // ── 可选输入：各自按连接状态独立开关 ──

            bool hasCascadeShadow = false;
            bool isScreenSpaceShadow = false;
            if (CascadeShadowMapSlot.IsConnected && CascadeShadowMapSlot.HasHandle)
            {
                hasCascadeShadow = true;
                if (ScreenSpaceShadowMapSlot.IsConnected && ScreenSpaceShadowMapSlot.HasHandle)
                {
                    isScreenSpaceShadow = true;
                }
            }

            bool hasLightDatas = LightDatasSlot?.IsConnected == true && LightDatasSlot.HasHandle;
            if (hasLightDatas)
            {
                passData.lightDatasBuffer = builder.ReadComputeBuffer(
                    LightDatasSlot.ReadHandle());
            }

            bool hasReflectionProbeAtlas = ReflectionProbeAtlasSlot?.IsConnected == true && ReflectionProbeAtlasSlot.HasHandle;
            if (hasReflectionProbeAtlas)
            {
                passData.reflectionProbeAtlas = builder.ReadTexture(
                    ReflectionProbeAtlasSlot.ReadHandle());
            }

            bool hasProbeMask = ProbeMaskSlot?.IsConnected == true && ProbeMaskSlot.HasHandle;
            if (hasProbeMask)
            {
                passData.probeMaskBuffer = builder.ReadComputeBuffer(
                    ProbeMaskSlot.ReadHandle());
            }

            bool hasProbeDatas = ProbeDatasSlot?.IsConnected == true && ProbeDatasSlot.HasHandle;
            if (hasProbeDatas)
            {
                passData.probeDatasBuffer = builder.ReadComputeBuffer(
                    ProbeDatasSlot.ReadHandle());
            }

            bool hasLightMask = LightMaskSlot?.IsConnected == true && LightMaskSlot.HasHandle;
            if (hasLightMask)
            {
                passData.lightMaskBuffer = builder.ReadComputeBuffer(
                    LightMaskSlot.ReadHandle());
            }

            // ── 渲染器列表：从解析后的句柄读取 ──

            passData.rendererList = builder.UseRendererList(rendererList);

            bool enableProbeKeyword = hasReflectionProbeAtlas && hasProbeMask && hasProbeDatas;

            passData.enableProbeKeyword = enableProbeKeyword;
            passData.hasLightMask = hasLightMask;
            passData.hasLightDatas = hasLightDatas;

            passData.viewMatrix = camera.worldToCameraMatrix;
            passData.projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            builder.SetRenderFunc(
                (DrawObjectPassData data, RenderGraphContext ctx) =>
                {
                    if (!IsEnabled)
                    {
                        return;
                    }

                    ctx.cmd.SetViewProjectionMatrices(data.viewMatrix, data.projMatrix);

                    if (!(camera.cameraType == CameraType.Preview && camera.name == HNRenderPipelineUtils.PREVIEW_CAMERA_NAME))
                    {
                        if (hasCascadeShadow)
                        {
                            ctx.cmd.EnableShaderKeyword(GlobalKeywords.cascadeShadowMap);
                            if (isScreenSpaceShadow)
                                ctx.cmd.EnableShaderKeyword(GlobalKeywords.screenSpaceShadowMap);
                            else
                                ctx.cmd.DisableShaderKeyword(GlobalKeywords.screenSpaceShadowMap);
                        }
                        else
                            ctx.cmd.DisableShaderKeyword(GlobalKeywords.cascadeShadowMap);

                        if (data.enableProbeKeyword)
                        {
                            ctx.cmd.EnableShaderKeyword(
                                GlobalKeywords.clusterCullingReflectionProbe);
                            ctx.cmd.SetGlobalTexture(
                                ClusterCullingReflectionProbePass.PropertyIDs.reflectionProbeAtlas,
                                data.reflectionProbeAtlas);
                            ctx.cmd.SetGlobalBuffer(
                                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeMaskBuffer,
                                data.probeMaskBuffer);
                            ctx.cmd.SetGlobalBuffer(
                                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeDatasBuffer,
                                data.probeDatasBuffer);
                        }
                        else
                        {
                            ctx.cmd.DisableShaderKeyword(GlobalKeywords.clusterCullingReflectionProbe);
                        }

                        // 簇剔除光照 shader keyword + 全局参数
                        if (data.hasLightMask)
                        {
                            ctx.cmd.EnableShaderKeyword(
                                GlobalKeywords.clusterCullingLight);
                            ctx.cmd.SetGlobalBuffer(
                                ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer,
                                data.lightMaskBuffer);
                        }

                        // 光照数据缓冲（仅 slot 连接时设置 ——
                        // 避免在无光照 pass 时绑定无效句柄）
                        if (data.hasLightDatas)
                        {
                            ctx.cmd.SetGlobalBuffer(
                                BuildLightDataPass.PropertyIDs.LightDatasBuffer,
                                data.lightDatasBuffer);
                        }
                    }

                    ctx.cmd.DrawRendererList(data.rendererList);
                });
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // 本 pass 不持有可释放资源。
        }

        // ── 辅助 ──

        /// <summary>
        /// 从 <see cref="RendererListParams"/> 本地创建渲染器列表。
        /// 裁剪结果不可用时返回默认（无效）句柄。
        /// </summary>
        /// <param name="renderGraph">要在其中创建列表的渲染图。</param>
        /// <returns>创建的渲染器列表句柄，或默认句柄。</returns>
        private RendererListHandle CreateRendererList(RenderGraph renderGraph)
        {
            if (!cameraContext.HasCullingResults)
            {
                return default;
            }

            RendererListDesc desc = rendererListParams.CreateDesc(
                ShaderPassNames.AllForwardNames,
                cameraContext.CullingResults,
                cameraContext.Camera);
            return renderGraph.CreateRendererList(desc);
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="DrawObjectPass"/> 的渲染图 pass 数据容器。
        /// </summary>
        private sealed class DrawObjectPassData
        {
            /// <summary>
            /// 颜色目标纹理句柄。
            /// </summary>
            public TextureHandle colorTarget;

            /// <summary>
            /// 深度目标纹理句柄。
            /// </summary>
            public TextureHandle depthTarget;

            /// <summary>
            /// 光照数据计算缓冲句柄。
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// 反射探针图集纹理句柄。
            /// </summary>
            public TextureHandle reflectionProbeAtlas;

            /// <summary>
            /// 簇剔除反射探针掩码缓冲句柄。
            /// </summary>
            public ComputeBufferHandle probeMaskBuffer;

            /// <summary>
            /// 簇剔除反射探针数据缓冲句柄。
            /// </summary>
            public ComputeBufferHandle probeDatasBuffer;

            /// <summary>
            /// 簇剔除光照掩码缓冲句柄。
            /// </summary>
            public ComputeBufferHandle lightMaskBuffer;

            /// <summary>
            /// 渲染器列表句柄。
            /// </summary>
            public RendererListHandle rendererList;

            /// <summary>
            /// 是否启用探针 keyword + 全局参数。
            /// </summary>
            public bool enableProbeKeyword;

            /// <summary>
            /// 是否绑定了簇剔除光照掩码缓冲。
            /// </summary>
            public bool hasLightMask;

            /// <summary>
            /// 是否绑定了光照数据缓冲。
            /// </summary>
            public bool hasLightDatas;

            /// <summary>
            /// 本 pass 相机的视图矩阵。
            /// </summary>
            public Matrix4x4 viewMatrix;

            /// <summary>
            /// 本 pass 相机的 GPU 投影矩阵。
            /// </summary>
            public Matrix4x4 projMatrix;
        }
    }
}
