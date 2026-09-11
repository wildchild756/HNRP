// <copyright file="DrawShadowPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 绘制所有光源阴影到一张 <see cref="Texture2DArray"/> 阴影图集。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每盏启用阴影的光源在 atlas 中占据一张或多张 map（方向光 = cascade、点光 = 6 面、
    /// 聚光 = 1 张）。位置由 <see cref="TextureAllocator"/> 分配，结果写入
    /// <c>_ShadowLightDatas</c> / <c>_ShadowMapDatas</c> 两张结构化缓冲供着色器两级查表。
    /// </para>
    /// <para>
    /// pass 由 <see cref="CameraRendererCache"/> 按相机复用，故 atlas / 分配器 / 驻留表
    /// 可跨帧保留。
    /// </para>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class DrawShadowPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。
        /// </summary>
        public const string PassNameConst = "Draw Shadow";

        // ── 常量 ──

        /// <summary>每盏光最多占用的 map 数（方向光 cascade / 点光面）。</summary>
        public const int MaxMapPerLight = 8;

        /// <summary>参与阴影渲染的方向光上限。</summary>
        private const int MaxDirectionalShadowLights = 4;

        /// <summary>参与阴影渲染的本地光（点 / 聚光）上限。</summary>
        private const int MaxLocalShadowLights = 32;

        /// <summary>点光阴影面的 fov 偏置（度），用于避免面与面之间的裂缝。</summary>
        private const float PointLightFovBias = 2.0f;

        // ── 可配置参数 ──

        /// <summary>
        /// atlas 单张 slice 的分辨率（正方形边长）。默认 4096。
        /// </summary>
        [SerializeField]
        private int sliceResolution = 4096;

        /// <summary>
        /// atlas 的 slice 数量（最多 4）。默认 1。
        /// </summary>
        [SerializeField]
        private int sliceCount = 1;

        /// <summary>
        /// 方向光阴影近平面偏移，用于避免投影面附近自阴影。
        /// </summary>
        [SerializeField]
        private float shadowNearPlaneOffset = 0.1f;

        /// <summary>阴影强度（写入 <c>shadowParams.x</c>）。</summary>
        [SerializeField]
        private float shadowStrength = 1f;

        /// <summary>软阴影系数（写入 <c>shadowParams.y</c>）。</summary>
        [SerializeField]
        private float shadowSoft = 0f;

        /// <summary>获取或设置 atlas 单 slice 分辨率。</summary>
        public int SliceResolution
        {
            get => sliceResolution;
            set => sliceResolution = value;
        }

        /// <summary>获取或设置 atlas slice 数量。</summary>
        public int SliceCount
        {
            get => sliceCount;
            set => sliceCount = value;
        }

        // ── Slot ──

        /// <summary>光照数据缓冲输入 slot（连接 <see cref="BuildLightDataPass"/>，只读）。</summary>
        public ComputeBufferSlot LightDatasBufferSlot { get; private set; }

        /// <summary>阴影图集输出 slot（连接下游 <see cref="DrawObjectPass"/> 的 ShadowMap）。</summary>
        public TextureSlot ShadowMapOutputSlot { get; private set; }

        // ── 每帧状态 ──

        private CameraContext cameraContext;

        // ── 持久资源 ──

        private RTHandle shadowAtlas;
        private int allocatedResolution;
        private int allocatedSliceCount;
        private TextureAllocator textureAllocator;

        private ComputeBuffer shadowLightDatasBuffer;
        private ComputeBuffer shadowMapDatasBuffer;
        private ShadowLightData[] shadowLightDatasArray;
        private ShadowMapData[] shadowMapDatasArray;
        private int lightDataCapacity;
        private int mapDataCapacity;

        /// <summary>驻留 map：key = MapIndex。</summary>
        private readonly Dictionary<uint, ResidentMap> residentMaps = new();

        /// <summary>本帧待释放的驻留 map（暂存，避免分配）。</summary>
        private readonly List<uint> releaseScratch = new List<uint>();

        /// <summary>本帧选中的光源（暂存）。</summary>
        private readonly List<SelectedLight> selectedLights = new List<SelectedLight>();

        /// <summary>本帧要绘制的阴影 slice（暂存）。</summary>
        private readonly List<ShadowDrawCommand> drawCommands = new List<ShadowDrawCommand>();

        /// <summary>本帧因 atlas 重分配需要搬移的 map（暂存）。</summary>
        private readonly List<ShadowCopyCommand> copyCommands = new List<ShadowCopyCommand>();

        /// <summary>局部清空阴影图区域用的材质（懒创建）。</summary>
        private Material shadowClearMaterial;

        /// <summary>OnDemand 更新请求（key = light 实例 id）。</summary>
        private static readonly HashSet<int> pendingOnDemandRequests = new HashSet<int>();

        /// <summary>分配器结果输出（暂存，避免分配）。</summary>
        private Dictionary<uint, TextureAllocatorResult> allocateResults =
            new Dictionary<uint, TextureAllocatorResult>();

        private ShadowGlobalParams shadowGlobalParams;

        // ── 构造函数 ──

        /// <summary>
        /// 无参构造仅供参数缓存 Pass 的反序列化使用。
        /// </summary>
        public DrawShadowPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="DrawShadowPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">本 pass 的实例名。</param>
        public DrawShadowPass(string passName)
            : base(passName)
        {
        }

        // ── 生命周期 ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            LightDatasBufferSlot = new ComputeBufferSlot("lightDatasBuffer", SlotDirection.Input);
            RegisterSlot(LightDatasBufferSlot);
            ShadowMapOutputSlot = new TextureSlot("ShadowMap", SlotDirection.Output);
            RegisterSlot(ShadowMapOutputSlot);
        }

        /// <inheritdoc />
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        public override void Record(RenderGraph renderGraph)
        {
            if (cameraContext == null || !cameraContext.HasCullingResults)
            {
                IsEnabled = false;
                return;
            }

            EnsureResources();

            BuildSelectedLights();
            UpdateResidentMaps();
            BuildTablesAndDrawCommands();

            RTHandle atlas = shadowAtlas;
            TextureHandle atlasHandle = renderGraph.ImportTexture(atlas);

            using var builder = renderGraph.AddRenderPass<DrawShadowPassData>(PassName, out var passData);
            builder.AllowPassCulling(false);

            passData.shadowMap = builder.WriteTexture(atlasHandle);

            if (LightDatasBufferSlot.IsConnected)
            {
                passData.lightDatasBuffer = builder.ReadComputeBuffer(LightDatasBufferSlot.ReadHandle());
            }

            if (ShadowMapOutputSlot != null)
            {
                ShadowMapOutputSlot.SetHandle(atlasHandle);
            }

            builder.SetRenderFunc((DrawShadowPassData data, RenderGraphContext ctx) =>
            {
                if (!IsEnabled)
                {
                    return;
                }

                RenderShadows(ctx, data, atlas);
            });
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // 注意：pass 每相机复用，此处仅在模板重建 / 渲染器销毁时被调用。
            shadowAtlas?.Release();
            shadowAtlas = null;

            shadowLightDatasBuffer?.Release();
            shadowLightDatasBuffer = null;
            shadowMapDatasBuffer?.Release();
            shadowMapDatasBuffer = null;

            CoreUtils.Destroy(shadowClearMaterial);
            shadowClearMaterial = null;

            textureAllocator = null;
            residentMaps.Clear();
            allocatedResolution = 0;
            allocatedSliceCount = 0;
            cameraContext = null;
        }

        /// <summary>
        /// 请求某盏灯在下一次 <see cref="DrawShadowPass"/> 执行时重绘其阴影（
        /// 用于 <see cref="ShadowUpdateModeType.OnDemand"/>）。
        /// </summary>
        /// <param name="light">要请求重绘的灯。</param>
        public static void RequestShadowUpdate(Light light)
        {
            if (light != null)
            {
                pendingOnDemandRequests.Add(light.GetInstanceID());
            }
        }

        // ── 资源 ──

        /// <summary>
        /// 确保持久资源（atlas、分配器、表缓冲）已按当前参数分配。
        /// </summary>
        private void EnsureResources()
        {
            if (shadowAtlas == null || allocatedResolution != sliceResolution || allocatedSliceCount != sliceCount)
            {
                shadowAtlas?.Release();

                int resolution = Mathf.Max(512, Mathf.ClosestPowerOfTwo(sliceResolution));
                int slices = Mathf.Clamp(sliceCount, 1, 4);

                shadowAtlas = RTHandles.Alloc(
                    resolution,
                    resolution,
                    slices: slices,
                    depthBufferBits: DepthBits.Depth32,
                    colorFormat: GraphicsFormat.None,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Clamp,
                    dimension: TextureDimension.Tex2DArray,
                    isShadowMap: true,
                    name: "HN Shadow Atlas");

                allocatedResolution = resolution;
                allocatedSliceCount = slices;

                textureAllocator = new TextureAllocator(resolution, 512, 4096, slices);
                residentMaps.Clear();

                // 整图初始化为远深度。
                var clearCmd = CommandBufferPool.Get("HN Shadow Atlas Clear");
                for (int slice = 0; slice < slices; slice++)
                {
                    clearCmd.SetRenderTarget((RenderTargetIdentifier)shadowAtlas, 0, CubemapFace.Unknown, slice);
                    clearCmd.ClearRenderTarget(false, true, Color.black);
                }
                cameraContext.Context.ExecuteCommandBuffer(clearCmd);
                clearCmd.Clear();
                CommandBufferPool.Release(clearCmd);
            }

            int maxLightCount = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                                + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;

            if (shadowLightDatasArray == null || lightDataCapacity != maxLightCount)
            {
                lightDataCapacity = maxLightCount;
                shadowLightDatasArray = new ShadowLightData[maxLightCount];
                HNRenderPipelineUtils.ValidateComputeBuffer(
                    ref shadowLightDatasBuffer, maxLightCount, System.Runtime.InteropServices.Marshal.SizeOf<ShadowLightData>());
            }

            int maxMapSlots = (allocatedResolution / 512) * (allocatedResolution / 512) * allocatedSliceCount;
            if (shadowMapDatasArray == null || mapDataCapacity != maxMapSlots)
            {
                mapDataCapacity = maxMapSlots;
                shadowMapDatasArray = new ShadowMapData[maxMapSlots];
                HNRenderPipelineUtils.ValidateComputeBuffer(
                    ref shadowMapDatasBuffer, maxMapSlots, System.Runtime.InteropServices.Marshal.SizeOf<ShadowMapData>());
            }
        }

        // ── 选灯 ──

        /// <summary>
        /// 收集本帧参与阴影的光源：方向光 main light 优先，其余按强度降序；
        /// 方向光 ≤4、本地光 ≤32。
        /// </summary>
        private void BuildSelectedLights()
        {
            selectedLights.Clear();

            NativeArray<VisibleLight> visibleLights = cameraContext.VisibleLights;
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
            {
                return;
            }

            int mainLightIndex = HNRenderPipelineUtils.GetMainLightIndex(visibleLights);
            int maxLightCount = Mathf.Min(
                visibleLights.Length,
                HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN);

            int directionalCount = 0;
            int localCount = 0;

            // main light 优先。
            if (mainLightIndex >= 0 && mainLightIndex < maxLightCount
                && TryAddShadowLight(visibleLights, mainLightIndex, ref directionalCount, ref localCount))
            {
            }

            for (int i = 0; i < maxLightCount; i++)
            {
                if (i == mainLightIndex)
                {
                    continue;
                }

                TryAddShadowLight(visibleLights, i, ref directionalCount, ref localCount);
            }
        }

        private bool TryAddShadowLight(
            NativeArray<VisibleLight> visibleLights,
            int index,
            ref int directionalCount,
            ref int localCount)
        {
            VisibleLight visibleLight = visibleLights[index];
            Light light = visibleLight.light;
            if (light == null || light.shadows == LightShadows.None)
            {
                return false;
            }

            if (!light.TryGetComponent(out HNAdditionalLightData additionalLightData)
                || !additionalLightData.EnableShadow)
            {
                return false;
            }

            LightType type = visibleLight.lightType;
            if (type == LightType.Directional)
            {
                if (directionalCount >= MaxDirectionalShadowLights)
                {
                    return false;
                }
                directionalCount++;
            }
            else if (type == LightType.Point || type == LightType.Spot)
            {
                if (localCount >= MaxLocalShadowLights)
                {
                    return false;
                }
                localCount++;
            }
            else
            {
                return false;
            }

            int resolution = HNRenderPipelineUtils.ClampShadowResolution(
                (int)additionalLightData.CascadeResolution, allocatedResolution);

            selectedLights.Add(new SelectedLight
            {
                lightIndex = index,
                lightType = type,
                resolution = resolution,
                cascadeCount = type == LightType.Directional
                    ? Mathf.Clamp((int)additionalLightData.CascadeCount, 1, CascadeShadowSettings.MaxCascadeCount)
                    : 0,
                cascadeSplits = additionalLightData.CascadeSplits,
                visibleLight = visibleLight,
                updateMode = additionalLightData.ShadowUpdateMode,
                timeSlices = additionalLightData.CascadeTimeSlices,
                lightInstanceId = light.GetInstanceID(),
            });

            return true;
        }

        // ── 驻留集 diff ──

        /// <summary>
        /// 同步驻留表与分配器：新增 map 分配、消失 map 释放、重分配结果回写。
        /// </summary>
        private void UpdateResidentMaps()
        {
            // 标记本帧仍需要的 map。
            for (int i = 0; i < selectedLights.Count; i++)
            {
                SelectedLight selected = selectedLights[i];
                int mapCount = GetMapCount(selected);
                for (int sub = 0; sub < mapCount; sub++)
                {
                    uint mapIndex = EncodeMapIndex(selected.lightIndex, sub);
                    if (!residentMaps.ContainsKey(mapIndex))
                    {
                        AllocateMap(mapIndex, selected);
                    }
                    else
                    {
                        ResidentMap resident = residentMaps[mapIndex];
                        if (resident.resolution != selected.resolution)
                        {
                            textureAllocator.Release(mapIndex);
                            residentMaps.Remove(mapIndex);
                            AllocateMap(mapIndex, selected);
                        }
                    }
                }
            }

            // 释放本帧不再需要的 map。
            releaseScratch.Clear();
            foreach (KeyValuePair<uint, ResidentMap> pair in residentMaps)
            {
                bool needed = false;
                for (int i = 0; i < selectedLights.Count; i++)
                {
                    SelectedLight selected = selectedLights[i];
                    if (selected.lightIndex != pair.Value.lightIndex)
                    {
                        continue;
                    }

                    if (pair.Value.subIndex < GetMapCount(selected))
                    {
                        needed = true;
                    }
                    break;
                }

                if (!needed)
                {
                    releaseScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < releaseScratch.Count; i++)
            {
                uint mapIndex = releaseScratch[i];
                textureAllocator.Release(mapIndex);
                residentMaps.Remove(mapIndex);
            }
        }

        private void AllocateMap(uint mapIndex, SelectedLight selected)
        {
            allocateResults.Clear();
            textureAllocator.Allocate(ref allocateResults, mapIndex, selected.resolution);

            // 处理被本次分配重分配（搬移）的既有 map：更新驻留位置并记录纹理搬移。
            foreach (KeyValuePair<uint, TextureAllocatorResult> pair in allocateResults)
            {
                if (pair.Key == mapIndex || !pair.Value.IsReorg)
                {
                    continue;
                }

                if (residentMaps.TryGetValue(pair.Key, out ResidentMap moved))
                {
                    copyCommands.Add(new ShadowCopyCommand
                    {
                        fromSlice = pair.Value.OldSliceIndex,
                        fromScaleOffset = pair.Value.OldScaleOffset,
                        toSlice = pair.Value.SliceIndex,
                        toScaleOffset = pair.Value.ScaleOffset,
                    });
                    moved.allocation = pair.Value;
                }
            }

            if (allocateResults.TryGetValue(mapIndex, out TextureAllocatorResult result))
            {
                residentMaps[mapIndex] = new ResidentMap
                {
                    lightIndex = selected.lightIndex,
                    subIndex = (int)(mapIndex & 0xFu),
                    lightType = selected.lightType,
                    resolution = selected.resolution,
                    allocation = result,
                    hasSignature = false,
                    lastUpdateFrame = -1,
                };
            }
        }

        private static int GetMapCount(SelectedLight selected)
        {
            switch (selected.lightType)
            {
                case LightType.Point:
                    return 6;
                case LightType.Spot:
                    return 1;
                default:
                    return Mathf.Clamp(selected.cascadeCount, 1, MaxMapPerLight);
            }
        }

        private static uint EncodeMapIndex(int lightIndex, int subIndex)
        {
            return ((uint)lightIndex << 4) | (uint)subIndex;
        }

        // ── 表与绘制命令 ──

        /// <summary>
        /// 填充 <c>ShadowLightData</c> / <c>ShadowMapData</c> 两张表，并生成本帧绘制命令。
        /// </summary>
        private void BuildTablesAndDrawCommands()
        {
            drawCommands.Clear();

            int mainLightIndex = HNRenderPipelineUtils.GetMainLightIndex(cameraContext.VisibleLights);
            int directionalCount = 0;
            int localCount = 0;

            // 清空方向光 cascade split 表，避免上一帧残留。
            for (int i = 0; i < selectedLights.Count; i++)
            {
                SelectedLight selected = selectedLights[i];
                if (selected.lightType == LightType.Directional)
                {
                    directionalCount++;
                }
                else
                {
                    localCount++;
                }
            }

            for (int i = 0; i < selectedLights.Count; i++)
            {
                SelectedLight selected = selectedLights[i];
                int lightIndex = selected.lightIndex;
                if (lightIndex < 0 || lightIndex >= shadowLightDatasArray.Length)
                {
                    continue;
                }

                ShadowLightData lightData = default;
                lightData.lightIndex = lightIndex;
                lightData.resolution = selected.resolution;
                lightData.cascadeSplits0 = Vector4.zero;
                lightData.cascadeSplits1 = Vector4.zero;

                int mapCount = GetMapCount(selected);
                bool allAllocated = true;
                for (int sub = 0; sub < mapCount; sub++)
                {
                    uint mapIndex = EncodeMapIndex(lightIndex, sub);
                    if (!residentMaps.TryGetValue(mapIndex, out ResidentMap resident))
                    {
                        allAllocated = false;
                        continue;
                    }

                    uint field = EncodeField(resident.allocation);
                    if (sub < 4)
                    {
                        lightData.blockDatas0 |= field << (sub * 8);
                    }
                    else
                    {
                        lightData.blockDatas1 |= field << ((sub - 4) * 8);
                    }

                    ShadowMapData mapData = BuildMapData(selected, resident, sub);
                    if ((int)field < shadowMapDatasArray.Length)
                    {
                        shadowMapDatasArray[field] = mapData;
                    }

                    bool shouldRedraw = ShouldRedrawMap(selected, resident, sub);
                    if (shouldRedraw && TryGetDrawCommand(selected, resident, sub, out ShadowDrawCommand command))
                    {
                        drawCommands.Add(command);
                    }
                }

                if (selected.lightType == LightType.Directional)
                {
                    lightData.cascadeSplits0 = BuildCascadeSplits(selected, 0);
                    lightData.cascadeSplits1 = BuildCascadeSplits(selected, 4);
                }

                // 任一 map 分配失败则禁用该 light 的阴影，避免着色器读到无效槽。
                if (!allAllocated)
                {
                    lightData.resolution = 0;
                }

                shadowLightDatasArray[lightIndex] = lightData;
            }

            shadowGlobalParams = new ShadowGlobalParams
            {
                _ShadowGlobalParams = new Vector4(mainLightIndex, lightDataCapacity, directionalCount, localCount),
                _ShadowGlobalParams2 = new Vector4(0f, allocatedResolution, 0f, 0f),
            };

            // OnDemand 请求本帧已消费，清空。
            pendingOnDemandRequests.Clear();
        }

        /// <summary>
        /// 判定某张 map 本帧是否需要重绘（新增 / 参数变化 / 更新模式）。
        /// </summary>
        private bool ShouldRedrawMap(SelectedLight selected, ResidentMap resident, int sub)
        {
            LightSignature signature = ComputeSignature(selected, sub, resident.resolution);
            bool redraw;

            if (!resident.hasSignature || !resident.signature.Equals(signature))
            {
                redraw = true;
            }
            else
            {
                switch (selected.updateMode)
                {
                    case ShadowUpdateModeType.OnDemand:
                        redraw = pendingOnDemandRequests.Contains(selected.lightInstanceId);
                        break;

                    case ShadowUpdateModeType.Custom:
                        int interval = GetTimeSlice(selected, sub);
                        redraw = interval <= 0 || Time.frameCount - resident.lastUpdateFrame >= interval;
                        break;

                    default:
                        redraw = true;
                        break;
                }
            }

            // 刚分配的 map 一定重绘。
            if (resident.lastUpdateFrame < 0)
            {
                redraw = true;
            }

            if (redraw)
            {
                resident.signature = signature;
                resident.hasSignature = true;
                resident.lastUpdateFrame = Time.frameCount;
            }

            return redraw;
        }

        private LightSignature ComputeSignature(SelectedLight selected, int sub, int resolution)
        {
            LightSignature signature = default;
            signature.lightMatrix = selected.visibleLight.localToWorldMatrix;
            signature.lightRange = selected.visibleLight.range;
            signature.spotAngle = selected.visibleLight.spotAngle;
            signature.resolution = resolution;
            signature.subIndex = sub;

            // 方向光 cascade 依赖相机，相机变化也需重绘。
            if (selected.lightType == LightType.Directional)
            {
                Camera camera = cameraContext.Camera;
                if (camera != null)
                {
                    signature.cameraView = camera.worldToCameraMatrix;
                    signature.cameraProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                }
            }

            return signature;
        }

        private static int GetTimeSlice(SelectedLight selected, int sub)
        {
            List<int> slices = selected.timeSlices;
            if (slices == null || slices.Count == 0)
            {
                return 0;
            }

            int index = Mathf.Clamp(sub, 0, slices.Count - 1);
            return slices[index];
        }

        private static uint EncodeField(TextureAllocatorResult allocation)
        {
            return ((uint)allocation.SliceIndex << 6) | (allocation.BlockId & 0x3Fu);
        }

        private Vector4 BuildCascadeSplits(SelectedLight selected, int offset)
        {
            List<float> splits = selected.cascadeSplits;
            Vector4 result = Vector4.zero;
            if (splits == null)
            {
                return result;
            }

            int count = Mathf.Clamp(selected.cascadeCount, 1, splits.Count);
            for (int i = 0; i < 4; i++)
            {
                int index = offset + i;
                if (index < count && index < splits.Count)
                {
                    result[i] = splits[index];
                }
            }

            return result;
        }

        private ShadowMapData BuildMapData(SelectedLight selected, ResidentMap resident, int sub)
        {
            ShadowMapData mapData = default;
            mapData.shadowParams = new Vector4(shadowStrength, shadowSoft, sub, 0f);

            if (ComputeMatrices(selected, sub, out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData splitData))
            {
                mapData.worldToShadow = GetShadowTransform(proj, view);
                Vector4 sphere = splitData.cullingSphere;
                mapData.cullingSphere = new Vector4(sphere.x, sphere.y, sphere.z, sphere.w * sphere.w);
            }

            return mapData;
        }

        private bool TryGetDrawCommand(
            SelectedLight selected,
            ResidentMap resident,
            int sub,
            out ShadowDrawCommand command)
        {
            command = default;
            if (!ComputeMatrices(selected, sub, out Matrix4x4 view, out Matrix4x4 proj, out ShadowSplitData splitData))
            {
                return false;
            }

            BatchCullingProjectionType projectionType = selected.lightType == LightType.Directional
                ? BatchCullingProjectionType.Orthographic
                : BatchCullingProjectionType.Perspective;

            command = new ShadowDrawCommand
            {
                view = view,
                proj = proj,
                allocation = resident.allocation,
                settings = new ShadowDrawingSettings(
                    cameraContext.CullingResults, selected.lightIndex, projectionType)
                {
                    splitData = splitData,
                },
            };
            return true;
        }

        /// <summary>
        /// 计算第 <paramref name="sub"/> 张 map 的视图 / 投影矩阵与裁剪数据。
        /// </summary>
        private bool ComputeMatrices(
            SelectedLight selected,
            int sub,
            out Matrix4x4 view,
            out Matrix4x4 proj,
            out ShadowSplitData splitData)
        {
            view = Matrix4x4.identity;
            proj = Matrix4x4.identity;
            splitData = default;

            CullingResults cullingResults = cameraContext.CullingResults;
            switch (selected.lightType)
            {
                case LightType.Directional:
                    return ComputeDirectionalMatrices(selected, sub, out view, out proj, out splitData);

                case LightType.Spot:
                {
                    int resolution = selected.resolution;
                    if (!cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                            selected.lightIndex, out view, out proj, out splitData))
                    {
                        return false;
                    }
                    splitData.shadowCascadeBlendCullingFactor = 1f;
                    return true;
                }

                case LightType.Point:
                {
                    if (!cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                            selected.lightIndex, (CubemapFace)sub, PointLightFovBias,
                            out view, out proj, out splitData))
                    {
                        return false;
                    }

                    // 对齐 URP：翻转 view 的第三行，修正点光面与聚光面法线偏置不一致。
                    view.m10 = -view.m10;
                    view.m11 = -view.m11;
                    view.m12 = -view.m12;
                    view.m13 = -view.m13;
                    splitData.shadowCascadeBlendCullingFactor = 1f;
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// 手写方向光级联矩阵：用相机子视锥拟合球体，再绕光方向建正交投影。
        /// 使用世界空间 split，支持任意级数。
        /// </summary>
        private bool ComputeDirectionalMatrices(
            SelectedLight selected,
            int cascadeIndex,
            out Matrix4x4 view,
            out Matrix4x4 proj,
            out ShadowSplitData splitData)
        {
            view = Matrix4x4.identity;
            proj = Matrix4x4.identity;
            splitData = default;

            Camera camera = cameraContext.Camera;
            List<float> splits = selected.cascadeSplits;
            if (camera == null || splits == null || cascadeIndex >= selected.cascadeCount)
            {
                return false;
            }

            float near = cascadeIndex == 0
                ? camera.nearClipPlane
                : splits[cascadeIndex - 1];
            float far = splits[Mathf.Min(cascadeIndex, splits.Count - 1)];
            if (far <= near)
            {
                far = near + 1f;
            }

            Vector3[] nearCorners = new Vector3[4];
            Vector3[] farCorners = new Vector3[4];
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), near, camera.stereoActiveEye, nearCorners);
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), far, camera.stereoActiveEye, farCorners);

            for (int i = 0; i < 4; i++)
            {
                nearCorners[i] = camera.transform.TransformPoint(nearCorners[i]);
                farCorners[i] = camera.transform.TransformPoint(farCorners[i]);
            }

            Vector3 center = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                center += nearCorners[i] + farCorners[i];
            }
            center /= 8f;

            float radius = 0f;
            for (int i = 0; i < 4; i++)
            {
                radius = Mathf.Max(radius, (nearCorners[i] - center).magnitude);
                radius = Mathf.Max(radius, (farCorners[i] - center).magnitude);
            }
            radius = Mathf.Max(radius, 0.001f);

            Vector3 lightForward = selected.visibleLight.localToWorldMatrix.GetColumn(2);
            Quaternion lightRotation = Quaternion.LookRotation(lightForward, Vector3.up);
            Vector3 viewPosition = center - lightForward * (radius + shadowNearPlaneOffset);

            view = Matrix4x4.TRS(viewPosition, lightRotation, Vector3.one).inverse;
            proj = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0f, 2f * radius + shadowNearPlaneOffset);

            splitData = new ShadowSplitData
            {
                cullingMatrix = proj * view,
                cullingSphere = new Vector4(center.x, center.y, center.z, radius),
                shadowCascadeBlendCullingFactor = 1f,
            };

            // 填充正交视锥的 6 个裁剪平面；否则 DrawShadows 无法正确裁剪 caster。
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(splitData.cullingMatrix);
            for (int i = 0; i < planes.Length; i++)
            {
                splitData.SetCullingPlane(i, planes[i]);
            }
            return true;
        }

        private static Matrix4x4 GetShadowTransform(Matrix4x4 proj, Matrix4x4 view)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                proj.m20 = -proj.m20;
                proj.m21 = -proj.m21;
                proj.m22 = -proj.m22;
                proj.m23 = -proj.m23;
            }

            Matrix4x4 worldToShadow = proj * view;
            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            return textureScaleAndBias * worldToShadow;
        }

        // ── 渲染 ──

        private void RenderShadows(RenderGraphContext ctx, DrawShadowPassData data, RTHandle atlas)
        {
            // 上传两张表并全局绑定。
            if (shadowLightDatasBuffer != null && shadowLightDatasArray != null)
            {
                ctx.cmd.SetBufferData(shadowLightDatasBuffer, shadowLightDatasArray);
                ctx.cmd.SetGlobalBuffer(PropertyIDs.shadowLightDatas, shadowLightDatasBuffer);
            }

            if (shadowMapDatasBuffer != null && shadowMapDatasArray != null)
            {
                ctx.cmd.SetBufferData(shadowMapDatasBuffer, shadowMapDatasArray);
                ctx.cmd.SetGlobalBuffer(PropertyIDs.shadowMapDatas, shadowMapDatasBuffer);
            }

            ctx.cmd.SetGlobalTexture(PropertyIDs.shadowMapArray, (RenderTargetIdentifier)atlas);
            ConstantBuffer.PushGlobal(ctx.cmd, shadowGlobalParams, PropertyIDs.shadowMapParamsBuffer);

            EnsureClearMaterial();

            // 1) 重分配搬移：把被移动 map 的旧区域内容复制到新区域。
            for (int i = 0; i < copyCommands.Count; i++)
            {
                ShadowCopyCommand copy = copyCommands[i];
                int size = Mathf.RoundToInt(copy.toScaleOffset.x * allocatedResolution);
                int srcX = Mathf.RoundToInt(copy.fromScaleOffset.z * allocatedResolution);
                int srcY = Mathf.RoundToInt(copy.fromScaleOffset.w * allocatedResolution);
                int dstX = Mathf.RoundToInt(copy.toScaleOffset.z * allocatedResolution);
                int dstY = Mathf.RoundToInt(copy.toScaleOffset.w * allocatedResolution);

                ctx.cmd.CopyTexture(
                    (RenderTargetIdentifier)atlas, copy.fromSlice, 0, srcX, srcY, size, size,
                    (RenderTargetIdentifier)atlas, copy.toSlice, 0, dstX, dstY);
            }

            // 2) 重绘：局部清远深度后绘制，未重绘的 map 内容跨帧保留。
            for (int i = 0; i < drawCommands.Count; i++)
            {
                ShadowDrawCommand command = drawCommands[i];
                int slice = command.allocation.SliceIndex;
                float scale = command.allocation.ScaleOffset.x;
                int resolution = Mathf.RoundToInt(scale * allocatedResolution);
                int offsetX = Mathf.RoundToInt(command.allocation.ScaleOffset.z * allocatedResolution);
                int offsetY = Mathf.RoundToInt(command.allocation.ScaleOffset.w * allocatedResolution);

                ctx.cmd.SetRenderTarget((RenderTargetIdentifier)atlas, 0, CubemapFace.Unknown, slice);
                ctx.cmd.SetViewport(new Rect(offsetX, offsetY, resolution, resolution));

                // 全屏三角形写远深度，受 viewport 限制只清本 map 区域。
                if (shadowClearMaterial != null)
                {
                    CoreUtils.DrawFullScreen(ctx.cmd, shadowClearMaterial, null, 0);
                }

                ctx.cmd.SetGlobalDepthBias(1.0f, 2.5f);
                ctx.cmd.SetViewProjectionMatrices(command.view, command.proj);

                ctx.renderContext.ExecuteCommandBuffer(ctx.cmd);
                ctx.cmd.Clear();
                ShadowDrawingSettings settings = command.settings;
                ctx.renderContext.DrawShadows(ref settings);

                ctx.cmd.DisableScissorRect();
                ctx.cmd.SetGlobalDepthBias(0f, 0f);
                ctx.renderContext.ExecuteCommandBuffer(ctx.cmd);
                ctx.cmd.Clear();
            }

            copyCommands.Clear();
        }

        private void EnsureClearMaterial()
        {
            if (shadowClearMaterial != null)
            {
                return;
            }

            Shader clearShader = cameraContext?.RuntimeResources?.shaderResources?.ShadowClear;
            if (clearShader != null)
            {
                shadowClearMaterial = new Material(clearShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
        }

        // ── 数据结构 ──

        private sealed class ResidentMap
        {
            public int lightIndex;
            public int subIndex;
            public LightType lightType;
            public int resolution;
            public TextureAllocatorResult allocation;

            /// <summary>上次重绘帧号；-1 表示刚分配尚未绘制。</summary>
            public int lastUpdateFrame;

            /// <summary>是否已记录参数签名。</summary>
            public bool hasSignature;

            /// <summary>光源 / 相机参数签名，用于检测是否需重绘。</summary>
            public LightSignature signature;
        }

        private struct SelectedLight
        {
            public int lightIndex;
            public LightType lightType;
            public int resolution;
            public int cascadeCount;
            public List<float> cascadeSplits;
            public VisibleLight visibleLight;
            public ShadowUpdateModeType updateMode;
            public List<int> timeSlices;
            public int lightInstanceId;
        }

        private struct ShadowDrawCommand
        {
            public Matrix4x4 view;
            public Matrix4x4 proj;
            public TextureAllocatorResult allocation;
            public ShadowDrawingSettings settings;
        }

        private struct ShadowCopyCommand
        {
            public int fromSlice;
            public Vector4 fromScaleOffset;
            public int toSlice;
            public Vector4 toScaleOffset;
        }

        /// <summary>
        /// 光源与相机参数签名，用于检测阴影是否需要重绘。
        /// </summary>
        private struct LightSignature : System.IEquatable<LightSignature>
        {
            public Matrix4x4 lightMatrix;
            public float lightRange;
            public float spotAngle;
            public int resolution;
            public int subIndex;
            public Matrix4x4 cameraView;
            public Matrix4x4 cameraProj;

            public bool Equals(LightSignature other)
            {
                return lightMatrix == other.lightMatrix
                    && lightRange == other.lightRange
                    && spotAngle == other.spotAngle
                    && resolution == other.resolution
                    && subIndex == other.subIndex
                    && cameraView == other.cameraView
                    && cameraProj == other.cameraProj;
            }

            public override bool Equals(object obj)
            {
                return obj is LightSignature other && Equals(other);
            }

            public override int GetHashCode()
            {
                return subIndex ^ resolution;
            }
        }

        private sealed class DrawShadowPassData
        {
            public TextureHandle shadowMap;
            public ComputeBufferHandle lightDatasBuffer;
        }

        // ── Property IDs ──

        /// <summary>本 pass 使用的 shader 属性标识。</summary>
        public static class PropertyIDs
        {
            /// <summary>阴影图集（Texture2DArray）。值：<c>_ShadowMapArray</c>。</summary>
            public static readonly int shadowMapArray = Shader.PropertyToID("_ShadowMapArray");

            /// <summary>按 light 的阴影元数据（StructuredBuffer）。值：<c>_ShadowLightDatas</c>。</summary>
            public static readonly int shadowLightDatas = Shader.PropertyToID("_ShadowLightDatas");

            /// <summary>按 atlas 槽的阴影数据（StructuredBuffer）。值：<c>_ShadowMapDatas</c>。</summary>
            public static readonly int shadowMapDatas = Shader.PropertyToID("_ShadowMapDatas");

            /// <summary>阴影全局参数常量缓冲。值：<c>_ShadowMapParamsBuffer</c>。</summary>
            public static readonly int shadowMapParamsBuffer = Shader.PropertyToID("_ShadowMapParamsBuffer");
        }
    }

    /// <summary>
    /// 按 light 的阴影元数据（供 GPU 两级查表的第一级）。
    /// 布局须与 shader 侧一致。
    /// </summary>
    public struct ShadowLightData
    {
        /// <summary>该 light 在可见光列表中的索引（= buffer 下标）。</summary>
        public int lightIndex;

        /// <summary>该 light 单张 map 的分辨率；0 表示该 light 无阴影。</summary>
        public int resolution;

        /// <summary>map 0..3 的 atlas 位置，每张 8 位 = (slice &lt;&lt; 6) | blockId。</summary>
        public uint blockDatas0;

        /// <summary>map 4..7 的 atlas 位置。</summary>
        public uint blockDatas1;

        /// <summary>方向光 cascade split 0..3（世界空间；0 表示无该级）。</summary>
        public Vector4 cascadeSplits0;

        /// <summary>方向光 cascade split 4..7。</summary>
        public Vector4 cascadeSplits1;
    }

    /// <summary>
    /// 按 atlas 槽的阴影数据（供 GPU 两级查表的第二级）。buffer 下标即 atlas 位置字段。
    /// 布局须与 shader 侧一致。
    /// </summary>
    public struct ShadowMapData
    {
        /// <summary>x=strength, y=soft, z=cascadeIndex/faceIndex, w=预留。</summary>
        public Vector4 shadowParams;

        /// <summary>世界 → 阴影裁剪矩阵（含 [0,1] remap，不含 atlas offset）。</summary>
        public Matrix4x4 worldToShadow;

        /// <summary>xyz=裁剪球心，w=半径平方（方向光选级）。</summary>
        public Vector4 cullingSphere;
    }

    /// <summary>
    /// 阴影全局参数常量缓冲。字段名须与 shader 侧 <c>_ShadowMapParamsBuffer</c> 一致。
    /// </summary>
    public struct ShadowGlobalParams
    {
        /// <summary>x=mainLightIndex, y=lightCount, z=方向光数, w=本地光数。</summary>
        public Vector4 _ShadowGlobalParams;

        /// <summary>x=shadowDistance, y=sliceResolution, zw=预留。</summary>
        public Vector4 _ShadowGlobalParams2;
    }
}
