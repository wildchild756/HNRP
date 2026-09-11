// <copyright file="ClusterCullingReflectionProbePass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    [Pass("Cluster Culling Probe")]
    public sealed class ClusterCullingReflectionProbePass : Pass, IGlobalShaderResource
    {
        /// <summary>
        /// 获取或设置反射探针图集的分配参数。
        /// </summary>
        public TextureResourceParams AtlasParams
        {
            get => atlasParams;
            set => atlasParams = value;
        }

        // ── Slot ──

        /// <summary>
        /// 获取反射探针图集的输入纹理 slot
        /// （<see cref="TextureSlot"/>，<see cref="SlotDirection.Input"/>）。
        /// 当连接了有效句柄时，本 pass 将八面体数据 blit 写入上游图集；
        /// 否则依据 <see cref="AtlasParams"/> 自行分配图集。
        /// 结果通过 <see cref="ReflectionProbeAtlasOutputSlot"/> 暴露给下游 pass。
        /// </summary>
        public TextureSlot ReflectionProbeAtlasInputSlot { get; private set; }

        /// <summary>
        /// 获取反射探针图集的输出纹理 slot
        /// （<see cref="TextureSlot"/>，<see cref="SlotDirection.Output"/>）。
        /// 写入完成后透传输入图集，下游 pass 无需单独的资源节点即可连接。
        /// </summary>
        public TextureSlot ReflectionProbeAtlasOutputSlot { get; private set; }

        /// <summary>
        /// 获取簇剔除反射探针掩码缓冲的输出计算缓冲 slot
        /// （<see cref="ComputeBufferSlot"/>，<see cref="SlotDirection.Output"/>）。
        /// </summary>
        public ComputeBufferSlot ClusterCullingReflectionProbeMaskBufferSlot { get; private set; }

        /// <summary>
        /// 获取簇剔除反射探针数据缓冲的输出计算缓冲 slot
        /// （<see cref="ComputeBufferSlot"/>，<see cref="SlotDirection.Output"/>）。
        /// </summary>
        public ComputeBufferSlot ClusterCullingReflectionProbeDatasBufferSlot { get; private set; }

        /// <summary>
        /// 获取或设置本帧已渲染的实时探针 cubemap 纹理字典，键为探针实例 id。
        /// 由渲染管线在 Phase B（实时探针渲染）完成后设置。
        /// 设置后，本 pass 使用这些纹理，而不再直接读取 <c>probe.realtimeTexture</c>。
        /// </summary>
        public IReadOnlyDictionary<int, Texture> RenderedProbeTextures { get; set; }

        // ── 可配置参数 ──

        /// <summary>
        /// 当 <see cref="ReflectionProbeAtlasInputSlot"/> 输入未连接/无效时，
        /// 用于分配反射探针图集的参数。默认：HDR 4096 图集、三线性 mip 过滤。
        /// </summary>
        [SerializeField]
        private TextureResourceParams atlasParams;

        // ── 相机上下文 ──

        private CameraContext cameraContext;
        private ComputeShader computeShader;

        // ── 可复用暂存缓冲（每帧零 GC）──
        // 渲染循环每帧填充这些预分配缓冲，而不新建数组/列表。
        // 懒初始化一次，永久复用。

        private List<ProbeEntry> probeEntries;
        private ReflectionProbeData4CS[] cullingDatas;
        private ClusterCullingReflectionProbeDatas[] sampleDatas;
        private int4[] scaleOffsetsInt;
        private Vector4[] scaleOffsetsUV;
        private Texture[] probeTextures;

        // ── 常量（对齐旧版 ClusterCullingReflectionProbePass）──

        private const int MaxReflectionProbesOnScreen = 64;
        private const int ReflectionProbeAtlasSize = 4096;
        private const int ReflectionProbeAtlasMipCount = 7;
        private const int ReflectionProbeAtlasTexelPadding = 2;
        private const int AtlasResolutionLevels = 5;
        private const uint MaxOffsetMask = 1u << 25;
        private const int MaxClusterMaskWords = 4096 * 4;
        private const string ClusterCullingKernelName = "ClusterCullingReflectionProbeCS";

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="ClusterCullingReflectionProbePass"/> 的新实例。
        /// pass 以默认 HDR 4096 图集参数启动；图集参数可经模板代码 /
        /// 编辑器缓存 / 运行时动态设置覆盖。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// 参数默认值与带名构造保持一致。
        /// </remarks>
        public ClusterCullingReflectionProbePass()
            : base(string.Empty)
        {
            atlasParams = CreateDefaultAtlasParams();
        }

        /// <summary>
        /// 初始化 <see cref="ClusterCullingReflectionProbePass"/> 的新实例。
        /// pass 以默认 HDR 4096 图集参数启动；
        /// <see cref="PreRecord"/> 每帧重新推导这些参数。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public ClusterCullingReflectionProbePass(string passName)
            : base(passName)
        {
            atlasParams = CreateDefaultAtlasParams();
        }

        /// <summary>
        /// 默认反射探针图集参数：HDR 4096、三线性 mip 过滤、Clamp 包裹。
        /// </summary>
        private static TextureResourceParams CreateDefaultAtlasParams()
        {
            return new TextureResourceParams
            {
                ColorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                DepthBits = DepthBits.None,
                Slices = 1,
                TextureScale = Vector2.one,
                Width = 4096,
                Height = 4096,
                FilterMode = FilterMode.Trilinear,
                WrapMode = TextureWrapMode.Clamp,
                TextureDimension = TextureDimension.Tex2D,
                UseMipMap = true,
                AutoGenerateMips = false,
                ClearBuffer = true,
                ClearColor = Color.black,
            };
        }

        // ── 生命周期 ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            ReflectionProbeAtlasInputSlot = new TextureSlot("reflectionProbeAtlas", SlotDirection.Input);
            RegisterSlot(ReflectionProbeAtlasInputSlot);
            ReflectionProbeAtlasOutputSlot = new TextureSlot("reflectionProbeAtlasOutput", SlotDirection.Output);
            RegisterSlot(ReflectionProbeAtlasOutputSlot);
            ClusterCullingReflectionProbeMaskBufferSlot = new ComputeBufferSlot(
                "clusterCullingReflectionProbeMaskBuffer", SlotDirection.Output);
            RegisterSlot(ClusterCullingReflectionProbeMaskBufferSlot);
            ClusterCullingReflectionProbeDatasBufferSlot = new ComputeBufferSlot(
                "clusterCullingReflectionProbeDatasBuffer", SlotDirection.Output);
            RegisterSlot(ClusterCullingReflectionProbeDatasBufferSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 保存相机上下文，并从 <see cref="CameraContext.RuntimeResources"/> 解析簇剔除
        /// compute shader。不再重置图集参数 —— 默认值在构造函数初始化，
        /// 逐帧重置会覆盖模板代码 / 编辑器缓存 / 运行时动态设置的参数。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;

            if (context.RuntimeResources != null)
            {
                computeShader = context.RuntimeResources.clusterCullingReflectionProbeCS;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 创建反射探针图集（纹理）、掩码缓冲与两个探针数据缓冲作为渲染图资源。
        /// 记录的渲染函数执行：
        /// <list type="bullet">
        ///   <item>上传可见实时探针数据（剔除包围盒 + 采样数据）</item>
        ///   <item>派发簇剔除 compute shader 填充掩码缓冲</item>
        ///   <item>将每个实时探针 cubemap blit 进其八面体图集区域</item>
        ///   <item>生成图集 mip 链</item>
        /// </list>
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            if (computeShader == null)
            {
                Debug.LogError(
                    "Cluster Culling Reflection Probe Compute Shader 为 null。 " +
                    "请确保已在管线资源的 HNRenderPipelineRuntimeResources 中赋值。");
                return;
            }

            if (cameraContext == null)
            {
                Debug.LogError("CameraContext 为 null。必须在 Record 前调用 Initialize。");
                return;
            }

            // ── 收集可见探针（烘焙 + 实时）并按旧版递归四等分布局打包进图集 ──
            // 可见的烘焙探针贡献其烘焙 cubemap；实时探针（在主相机之前的
            // Phase B 渲染）贡献其实时 cubemap。每个可见探针每帧都 blit 一次，
            // 因为图集是渲染图的瞬态资源。
            EnsureScratchBuffers();
            List<ProbeEntry> entries = probeEntries;
            entries.Clear();
            if (cameraContext.VisibleReflectionProbes.IsCreated)
            {
                var visibleProbes = cameraContext.VisibleReflectionProbes;
                for (int i = 0; i < visibleProbes.Length; i++)
                {
                    UnityEngine.Rendering.VisibleReflectionProbe visibleProbe = visibleProbes[i];
                    ReflectionProbe probe = ReflectionProbeRenderUtils.GetReflectionProbe(visibleProbe);
                    if (probe == null)
                    {
                        continue;
                    }

                    // HNRP 自己管理实时探针的 cubemap（见 ReflectionProbeRenderer），
                    // Unity 的 VisibleReflectionProbe.texture 对这类探针为空，
                    // 因此仍按 mode 从 component 取纹理：
                    //   Realtime -> realtimeTexture；
                    //   Baked / Custom -> customBakedTexture。
                    // 时间切片只控制 cubemap 何时重渲染，不影响图集是否包含该探针。
                    Texture texture;
                    if (ReflectionProbeRenderUtils.IsRealtimeProbe(probe))
                    {
                        texture = probe.realtimeTexture;
                    }
                    else if (ReflectionProbeRenderUtils.IsBakedProbe(probe) || ReflectionProbeRenderUtils.IsCustomBakedProbe(probe))
                    {
                        texture = probe.customBakedTexture;
                    }
                    else
                    {
                        continue;
                    }

                    if (texture == null)
                    {
                        continue;
                    }

                    int level = AtlasLevelForResolution(probe.resolution);
                    if (level < 0 || level >= AtlasResolutionLevels)
                    {
                        continue;
                    }

                    entries.Add(new ProbeEntry { Probe = probe, Texture = texture, Level = level });
                }
            }

            int probeCount = Mathf.Min(entries.Count, MaxReflectionProbesOnScreen);

            uint offsetMask = 0;
            int probeIndex = 0;
            for (int level = 0; level < AtlasResolutionLevels && probeIndex < probeCount; level++)
            {
                int width = ReflectionProbeAtlasSize / (int)Mathf.Pow(2, level);
                for (int i = 0; i < entries.Count && probeIndex < probeCount; i++)
                {
                    ProbeEntry entry = entries[i];
                    if (entry.Level != level)
                    {
                        continue;
                    }

                    if (offsetMask >= MaxOffsetMask)
                    {
                        break;
                    }

                    GetOffset(offsetMask, out int offsetX, out int offsetY);
                    var scaleOffsetInt = new int4(width, width, offsetX, offsetY);

                    ReflectionProbe probe = entry.Probe;
                    Bounds bounds = probe.bounds;
                    Vector4 scaleOffsetUV = GetTextureScaleOffsetWithoutPaddingInAtlas(scaleOffsetInt);

                    // Unity 对反射探针的可见性剔除按 bounds 外扩 blendDistance 计算
                    //（VisibleReflectionProbe 的收录范围就是 box + blendDistance）。
                    // 逐簇剔除必须用同一个外扩后的包围盒：否则当原始 box 离开视锥、
                    // 而 box + blendDistance 仍与视锥相交时，所有簇都会被清空，
                    // 整个探针被错误剔除，表现为相机移动时的跳变。
                    // 注意：shader 的权重盒仍用原始 bounds（boxMin/boxMax），
                    // 外扩只用于剔除包围盒。
                    Vector3 cullExtents = bounds.extents + Vector3.one * probe.blendDistance;
                    cullingDatas[probeIndex] = new ReflectionProbeData4CS
                    {
                        boundCenter = bounds.center,
                        boundExtents = cullExtents,
                    };

                    sampleDatas[probeIndex] = new ClusterCullingReflectionProbeDatas
                    {
                        boxMax = bounds.max,
                        boxMin = bounds.min,
                        positionWS = probe.transform.position,
                        blendDistance = probe.blendDistance,
                        importance = probe.importance,
                        intensity = probe.intensity,
                        scaleOffset = scaleOffsetUV,
                        mipCount = Mathf.Log(probe.resolution, 2.0f),
                    };

                    scaleOffsetsInt[probeIndex] = scaleOffsetInt;
                    scaleOffsetsUV[probeIndex] = scaleOffsetUV;
                    probeTextures[probeIndex] = entry.Texture;

                    probeIndex++;
                    offsetMask += (uint)1 << (int)(Mathf.Log(width, 2) * 2 - 2);
                }
            }

            probeCount = probeIndex;

            // ── 输入/输出：反射探针图集（必须在 AddRenderPass 之前解析并校验）──
            // 当输入句柄有效时消费所连接的输入图集；否则依据 AtlasParams
            // 本地分配图集。blit 后的八面体数据写入其中，再暴露给下游 pass。
            // 图集无效时直接跳过本帧、不产生 pass，避免执行期抛
            // "was not provided with an execute function"。

            TextureHandle atlasHandle;
            if (ReflectionProbeAtlasInputSlot != null
                && ReflectionProbeAtlasInputSlot.IsConnected
                && ReflectionProbeAtlasInputSlot.HasHandle)
            {
                atlasHandle = ReflectionProbeAtlasInputSlot.ReadHandle();
            }
            else
            {
                atlasHandle = renderGraph.CreateTexture(
                    atlasParams.CreateDesc("Reflection Probe Atlas", cameraContext.Camera));
            }

            if (!atlasHandle.IsValid())
            {
                return;
            }

            // 透传到输出 slot，供下游 pass 使用。
            if (ReflectionProbeAtlasOutputSlot != null)
            {
                ReflectionProbeAtlasOutputSlot.SetHandle(atlasHandle);
            }

            using (var builder = renderGraph.AddRenderPass<ClusterCullingReflectionProbePassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                passData.reflectionProbeAtlas = builder.WriteTexture(atlasHandle);

                // ── 输出：掩码缓冲 ──

                ComputeBufferHandle maskHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxClusterMaskWords,
                        sizeof(uint))
                    { name = "Cluster Culling Reflection Probe Mask Buffer" });

                passData.clusterCullingReflectionProbeMaskBuffer = builder.WriteComputeBuffer(maskHandle);

                // ── 输出：剔除数据缓冲（ReflectionProbeData4CS 布局）──

                ComputeBufferHandle cullingDatasHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxReflectionProbesOnScreen,
                        UnsafeUtility.SizeOf<ReflectionProbeData4CS>())
                    { name = "Cluster Culling Reflection Probe Culling Datas Buffer" });

                passData.cullingDatasBuffer = builder.WriteComputeBuffer(cullingDatasHandle);

                // ── 输出：采样数据缓冲（ClusterCullingReflectionProbeDatas 布局）──

                ComputeBufferHandle sampleDatasHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxReflectionProbesOnScreen,
                        UnsafeUtility.SizeOf<ClusterCullingReflectionProbeDatas>())
                    { name = "Cluster Culling Reflection Probe Datas Buffer" });

                passData.sampleDatasBuffer = builder.WriteComputeBuffer(sampleDatasHandle);

                // ── 把真实渲染图句柄发布到输出 slot ──

                ClusterCullingReflectionProbeMaskBufferSlot!.SetHandle(maskHandle);
                ClusterCullingReflectionProbeDatasBufferSlot!.SetHandle(sampleDatasHandle);

                // ── compute shader 配置 ──

                passData.clusterCullingReflectionProbeCS = computeShader;
                passData.clusterCullingKernel = computeShader.FindKernel(ClusterCullingKernelName);

                Camera camera = cameraContext.Camera;
                int2 screenResolution = math.int2(camera.pixelWidth, camera.pixelHeight);
                int3 clusterSize = GetClusterSize(screenResolution);
                float2 clusterZScaleOffset = GetClusterZScaleOffset(
                    clusterSize, camera.orthographic,
                    camera.nearClipPlane, camera.farClipPlane);

                int itemsPerCluster = MaxReflectionProbesOnScreen;
                int wordsPerCluster = (itemsPerCluster + 31) / 32 + 1;

                // compute shader 通过 GPU 投影（D3D 风格，z 在 [0,1]）把簇切片深度
                // 变换到裁剪空间，再到世界空间与探针包围盒做 AABB 重叠测试。
                // 若直接使用原始 OpenGL projectionMatrix（NDC z 在 [-1,1]），
                // 一半裁剪 z 范围会低于 shader 的 [0,1] clamp，导致重叠测试失败。
                // HNRP 所有 pass 均经 RenderGraph 渲染，内部总是渲染到渲染纹理，
                // 因此 renderIntoTexture 恒为 true。
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(
                    camera.projectionMatrix, true);
                Matrix4x4 clipToView = gpuProj.inverse;
                Matrix4x4 viewToClip = gpuProj;
                Matrix4x4 clipToWorld = (gpuProj * camera.worldToCameraMatrix).inverse;

                // ── 每帧参数（经 PushGlobal 上传到 shader）──

                passData.clusterCullingReflectionProbeParams.clusterSizeXY =
                    new Vector2(clusterSize.x, clusterSize.y);
                passData.clusterCullingReflectionProbeParams.clusterZScaleOffset =
                    new Vector2(clusterZScaleOffset.x, clusterZScaleOffset.y);
                passData.clusterCullingReflectionProbeParams.wordsPerCluster =
                    wordsPerCluster;
                passData.clusterCullingReflectionProbeParams.reflectionProbeCount =
                    probeCount;
                passData.clusterCullingReflectionProbeParams.unused0 = 0.0f;
                passData.clusterCullingReflectionProbeParams.unused1 = 0.0f;

                // ── 把每帧值存到池化 pass data 上，
                // 使渲染函数闭包只捕获 `this`（零分配）──

                passData.clusterSize = clusterSize;
                passData.probeCount = probeCount;
                passData.cameraOrthographic = camera.orthographic;
                passData.clipToView = clipToView;
                passData.viewToClip = viewToClip;
                passData.clipToWorld = clipToWorld;

                // ── 渲染函数 ──

                builder.SetRenderFunc(
                    (ClusterCullingReflectionProbePassData data, RenderGraphContext ctx) =>
                    {
                        if(!IsEnabled)
                        {
                            return;
                        }

                        // 上传剔除派发与 shader 使用的探针数据。
                        ctx.cmd.SetBufferData(data.cullingDatasBuffer, this.cullingDatas);
                        ctx.cmd.SetBufferData(data.sampleDatasBuffer, this.sampleDatas);

                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingReflectionProbeCS,
                            data.clusterCullingKernel,
                            PropertyIDs.clusterCullingReflectionProbeMaskBuffer,
                            data.clusterCullingReflectionProbeMaskBuffer);
                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingReflectionProbeCS,
                            data.clusterCullingKernel,
                            PropertyIDs.reflectionProbeDatas4CSBuffer,
                            data.cullingDatasBuffer);

                        Vector2 clusterZScaleOffsetInPass = data.clusterCullingReflectionProbeParams.clusterZScaleOffset;
                        int wordsPerClusterInPass = data.clusterCullingReflectionProbeParams.wordsPerCluster;
                        int3 clusterSizeInPass = data.clusterSize;
                        int probeCountInPass = data.probeCount;

                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingReflectionProbeCS,
                            PropertyIDs.cullingParams0,
                            new Vector4(
                                clusterZScaleOffsetInPass.x,
                                clusterZScaleOffsetInPass.y,
                                wordsPerClusterInPass,
                                data.cameraOrthographic ? 1.0f : 0.0f));
                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingReflectionProbeCS,
                            PropertyIDs.cullingParams1,
                            new Vector4(clusterSizeInPass.x, clusterSizeInPass.y, clusterSizeInPass.z, probeCountInPass));

                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingReflectionProbeCS,
                            PropertyIDs.cullingClipToViewMatrix,
                            data.clipToView);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingReflectionProbeCS,
                            PropertyIDs.cullingViewToClipMatrix,
                            data.viewToClip);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingReflectionProbeCS,
                            PropertyIDs.cullingClipToWorldMatrix,
                            data.clipToWorld);

                        // 在整个 3D 网格上按每簇一个线程派发。
                        // numthreads(8,8,1)：线程组覆盖 x/y，z 向线程组覆盖每个深度
                        // 切片（id.z 即簇索引的 z）。
                        int threadGroupX = (clusterSizeInPass.x + 7) / 8;
                        int threadGroupY = (clusterSizeInPass.y + 7) / 8;
                        ctx.cmd.DispatchCompute(
                            data.clusterCullingReflectionProbeCS,
                            data.clusterCullingKernel,
                            threadGroupX,
                            threadGroupY,
                            clusterSizeInPass.z);

                        // ── 把每个可见探针 cubemap blit 进其图集区域 ──
                        // 八面体投影被绘制到区域 mip 0（已计入 padding），
                        // 再由 GenerateMips 生成其余图集 mip 链。
                        // 这样不依赖源 cubemap 自带有效 mip（实时 cubemap 可能没有）。
                        for (int i = 0; i < probeCountInPass; i++)
                        {
                            Texture source = this.probeTextures[i];
                            if (source == null)
                            {
                                continue;
                            }

                            int texelPadding = ReflectionProbeAtlasTexelPadding;
                            Vector2 textureSizeWithoutPadding =
                                GetTextureSizeWithoutPadding(this.scaleOffsetsUV[i], texelPadding);

                            ctx.cmd.SetRenderTarget(
                                (RenderTargetIdentifier)data.reflectionProbeAtlas);
                            var propertyBlock = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                            Blitter.BlitCubeToOctahedral2DQuadWithPadding(
                                ctx.cmd,
                                propertyBlock,
                                source,
                                textureSizeWithoutPadding,
                                this.scaleOffsetsUV[i],
                                0,
                                true,
                                texelPadding);
                        }

                        if (probeCountInPass > 0)
                        {
                            ctx.cmd.GenerateMips((RenderTexture)data.reflectionProbeAtlas);
                        }

                        // 上传簇剔除参数，使片元 shader 能解析簇索引来迭代探针。
                        ConstantBuffer.PushGlobal(
                            ctx.cmd,
                            data.clusterCullingReflectionProbeParams,
                            PropertyIDs.clusterCullingReflectionProbeParamsBuffer);
                    });
            }
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // 本 pass 不持有可释放资源。
            computeShader = null;
            cameraContext = null;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 图集、掩码缓冲与探针数据缓冲三者齐备（且本 pass 启用）时，开启
        /// <c>CLUSTER_CULLING_REFLECTION_PROBE</c> keyword 并绑定对应全局资源；
        /// 否则关闭该 keyword，避免全局状态残留。
        /// </remarks>
        public void BindGlobalShaderResources(CommandBuffer cmd)
        {
            bool available = IsEnabled
                && ReflectionProbeAtlasOutputSlot != null
                && ReflectionProbeAtlasOutputSlot.HasHandle
                && ClusterCullingReflectionProbeMaskBufferSlot != null
                && ClusterCullingReflectionProbeMaskBufferSlot.HasHandle
                && ClusterCullingReflectionProbeDatasBufferSlot != null
                && ClusterCullingReflectionProbeDatasBufferSlot.HasHandle;

            if (!available)
            {
                cmd.DisableShaderKeyword(GlobalKeywords.clusterCullingReflectionProbe);
                return;
            }

            cmd.EnableShaderKeyword(GlobalKeywords.clusterCullingReflectionProbe);
            cmd.SetGlobalTexture(
                PropertyIDs.reflectionProbeAtlas,
                ReflectionProbeAtlasOutputSlot.ReadHandle());
            cmd.SetGlobalBuffer(
                PropertyIDs.clusterCullingReflectionProbeMaskBuffer,
                ClusterCullingReflectionProbeMaskBufferSlot.ReadHandle());
            cmd.SetGlobalBuffer(
                PropertyIDs.clusterCullingReflectionProbeDatasBuffer,
                ClusterCullingReflectionProbeDatasBufferSlot.ReadHandle());
        }

        // ── 暂存缓冲辅助 ──

        /// <summary>
        /// 懒分配每帧暂存缓冲，一次分配永久复用，使渲染循环保持零分配。
        /// </summary>
        private void EnsureScratchBuffers()
        {
            if (probeEntries == null)
            {
                probeEntries = new List<ProbeEntry>(MaxReflectionProbesOnScreen);
                cullingDatas = new ReflectionProbeData4CS[MaxReflectionProbesOnScreen];
                sampleDatas = new ClusterCullingReflectionProbeDatas[MaxReflectionProbesOnScreen];
                scaleOffsetsInt = new int4[MaxReflectionProbesOnScreen];
                scaleOffsetsUV = new Vector4[MaxReflectionProbesOnScreen];
                probeTextures = new Texture[MaxReflectionProbesOnScreen];
            }
        }

        // ── 图集布局辅助（旧版递归四等分布局）──

        /// <summary>
        /// 把探针分辨率映射为图集层级：<c>0..4</c> 分别对应每探针
        /// <c>4096..256</c> 纹素。分辨率小于等于 0 时返回 <c>-1</c>；
        /// 超出层级范围（如 4096）时钳制到最近可用层级，
        /// 避免整个探针被静默剔除。
        /// </summary>
        private static int AtlasLevelForResolution(int resolution)
        {
            if (resolution <= 0)
            {
                return -1;
            }

            int log2 = (int)(Mathf.Log(resolution, 2) + 0.5f);
            int level = 11 - log2;
            return Mathf.Clamp(level, 0, AtlasResolutionLevels - 1);
        }

        /// <summary>
        /// 依据递归四等分 <paramref name="offsetMask"/> 计算探针的图集区域偏移。
        /// 掩码按位存储细分路径：有效位为中间 2 * 5 = 10 位（15..24）；
        /// 相邻位对（低为 x、高为 y）编码递归四分之一划分，
        /// 支持 4096 到 256 的探针分辨率。
        /// </summary>
        private static void GetOffset(uint offsetMask, out int offsetX, out int offsetY)
        {
            offsetX = offsetY = 0;
            uint oddBits = 0;
            uint evenBits = 0;
            int oddIndex = 0;
            int evenIndex = 0;
            for (int i = 0; i < 32; i++)
            {
                uint bit = (offsetMask >> i) & 0x1;
                if (i % 2 == 0)
                {
                    evenIndex++;
                    evenBits |= (bit << evenIndex);
                }
                else
                {
                    oddIndex++;
                    oddBits |= (bit << oddIndex);
                }
            }

            offsetX = (int)evenBits;
            offsetY = (int)oddBits;
        }

        /// <summary>
        /// 把整数图集区域（尺寸 + 纹素偏移）转换为不含 padding 的归一化
        /// 缩放/偏移向量——即存入数据缓冲、由 <c>GetReflectionProbeAtlasUV</c>
        /// 消费的值。
        /// </summary>
        private static Vector4 GetTextureScaleOffsetWithoutPaddingInAtlas(int4 scaleOffset)
        {
            float atlasSize = ReflectionProbeAtlasSize;
            float scaleX = scaleOffset.x / atlasSize;
            float scaleY = scaleOffset.y / atlasSize;
            float offsetX = scaleOffset.z / atlasSize;
            float offsetY = scaleOffset.w / atlasSize;
            return new Vector4(scaleX, scaleY, offsetX, offsetY);
        }

        /// <summary>
        /// 依据归一化图集区域计算源纹理尺寸（纹素，不含 padding）。
        /// </summary>
        private static Vector2 GetTextureSizeWithoutPadding(Vector4 scaleOffset, int texelPadding)
        {
            float scaleX = scaleOffset.x * ReflectionProbeAtlasSize - texelPadding * 2;
            float scaleY = scaleOffset.y * ReflectionProbeAtlasSize - texelPadding * 2;
            return new Vector2(scaleX, scaleY);
        }

        /// <summary>
        /// 本帧已排入图集打包的可见探针。
        /// </summary>
        private struct ProbeEntry
        {
            public ReflectionProbe Probe;
            public Texture Texture;
            public int Level;
        }

        // ── 簇尺寸计算 ──

        private const int ClusterMinTileSize = 8;
        private const int ClusterMaxZSlice = 128;
        private const int ClusterMinZSlice = 16;

        /// <summary>
        /// cluster 网格计算的最小屏幕分辨率。低于此值（0、极小窗口、预览相机）
        /// 会使 tileCountPerSlice 为 0 导致除零 / 死循环，故钳制。
        /// </summary>
        private const int MinClusterScreenResolution = 128;

        private static int3 GetClusterSize(int2 screenResolution)
        {
            // 退化分辨率（0 或极小，如窗口最小化 / 预览相机）会让 clusterSizeXY
            // 坍缩为非正值，使 tileCountPerSlice 为 0 → 除零 / 死循环。
            // 钳制到最小分辨率，保证计算可终止且结果为合法正值。
            screenResolution = math.max(
                screenResolution, new int2(MinClusterScreenResolution));

            // 每个簇在掩码缓冲中存 wordsPerCluster 个 uint
            // （header + 每 32 个探针位一个字）。切片数量必须由掩码缓冲
            // 容量除以每簇字数得出，否则 compute shader 会写出缓冲末尾。
            int wordsPerCluster = (MaxReflectionProbesOnScreen + 31) / 32 + 1;
            int2 clusterSizeXY = new int2(1, 1);
            int sliceCount = ClusterMinZSlice;
            int tileWidth = ClusterMinTileSize >> 1;
            do
            {
                tileWidth <<= 1;
                clusterSizeXY = (screenResolution + tileWidth - 1) / tileWidth;
                int tileCountPerSlice = clusterSizeXY.x * clusterSizeXY.y;
                sliceCount = MaxClusterMaskWords / (tileCountPerSlice * wordsPerCluster) - 1;
            }
            while (sliceCount < ClusterMinZSlice || sliceCount > ClusterMaxZSlice);

            return new int3(clusterSizeXY.x, clusterSizeXY.y, sliceCount);
        }

        private static float2 GetClusterZScaleOffset(
            int3 clusterSize, bool isOrthographic,
            float nearClipPlane, float farClipPlane)
        {
            float2 result;
            if (isOrthographic)
            {
                result.x = (float)clusterSize.z / (farClipPlane - nearClipPlane);
                result.y = -nearClipPlane * result.x;
            }
            else
            {
                result.x = (float)clusterSize.z / (math.log2(farClipPlane) - math.log2(nearClipPlane));
                result.y = -math.log2(nearClipPlane) * result.x;
            }

            return result;
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="ClusterCullingReflectionProbePass"/> 的渲染图 pass 数据容器。
        /// </summary>
        private sealed class ClusterCullingReflectionProbePassData
        {
            /// <summary>
            /// 反射探针图集纹理句柄。
            /// </summary>
            public TextureHandle reflectionProbeAtlas;

            /// <summary>
            /// 簇剔除掩码缓冲句柄。
            /// </summary>
            public ComputeBufferHandle clusterCullingReflectionProbeMaskBuffer;

            /// <summary>
            /// 簇剔除探针剔除数据缓冲句柄
            /// （<see cref="ReflectionProbeData4CS"/> 布局，喂给 compute shader）。
            /// </summary>
            public ComputeBufferHandle cullingDatasBuffer;

            /// <summary>
            /// 簇剔除探针采样数据缓冲句柄
            /// （<see cref="ClusterCullingReflectionProbeDatas"/> 布局，由 shader 消费）。
            /// </summary>
            public ComputeBufferHandle sampleDatasBuffer;

            /// <summary>
            /// 簇剔除 compute shader。
            /// </summary>
            public ComputeShader clusterCullingReflectionProbeCS;

            /// <summary>
            /// 剔除派发使用的 kernel 索引。
            /// </summary>
            public int clusterCullingKernel;

            /// <summary>
            /// 经 <c>_ClusterCullingReflectionProbeParamsBuffer</c> 上传到
            /// shader 的簇剔除参数。
            /// </summary>
            public ClusterCullingReflectionProbeParams clusterCullingReflectionProbeParams;

            /// <summary>
            /// 本帧簇网格尺寸。
            /// </summary>
            public int3 clusterSize;

            /// <summary>
            /// 本帧打包进图集的探针数量。
            /// </summary>
            public int probeCount;

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

        // ── shader 属性 ID ──

        /// <summary>
        /// 簇剔除反射探针 compute shader 参数的 shader 属性标识。
        /// 与簇剔除反射探针 compute shader 使用的属性 ID 保持一致。
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// 反射探针图集纹理。值：<c>_ReflectionProbeAtlas</c>。
            /// </summary>
            public static readonly int reflectionProbeAtlas =
                Shader.PropertyToID("_ReflectionProbeAtlas");

            /// <summary>
            /// 簇剔除反射探针掩码缓冲（RWStructuredBuffer）。
            /// 值：<c>_ClusterCullingReflectionProbeMaskBuffer</c>。
            /// </summary>
            public static readonly int clusterCullingReflectionProbeMaskBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeMaskBuffer");

            /// <summary>
            /// 簇剔除反射探针数据缓冲（RWStructuredBuffer）。
            /// 值：<c>_ClusterCullingReflectionProbeDatasBuffer</c>。
            /// </summary>
            public static readonly int clusterCullingReflectionProbeDatasBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeDatasBuffer");

            /// <summary>
            /// 剔除参数 0：x=z 缩放、y=z 偏移、z=wordsPerCluster、w=isOrthographic。
            /// 值：<c>_ClusterCullingReflectionProbeParams0</c>。
            /// </summary>
            public static readonly int cullingParams0 =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParams0");

            /// <summary>
            /// 剔除参数 1：xyz=clusterSize、w=probeCount。
            /// 值：<c>_ClusterCullingReflectionProbeParams1</c>。
            /// </summary>
            public static readonly int cullingParams1 =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParams1");

            /// <summary>
            /// 裁剪空间到视图空间矩阵。值：<c>_ClusterCullingReflectionProbeClipToView</c>。
            /// </summary>
            public static readonly int cullingClipToViewMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeClipToView");

            /// <summary>
            /// 视图空间到裁剪空间矩阵。值：<c>_ClusterCullingReflectionProbeViewToClip</c>。
            /// </summary>
            public static readonly int cullingViewToClipMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeViewToClip");

            /// <summary>
            /// 裁剪空间到世界空间矩阵。值：<c>_ClusterCullingReflectionProbeClipToWorld</c>。
            /// </summary>
            public static readonly int cullingClipToWorldMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeClipToWorld");

            /// <summary>
            /// 簇剔除反射探针参数缓冲（探针参数结构化缓冲）。
            /// 值：<c>_ClusterCullingReflectionProbeParamsBuffer</c>。
            /// </summary>
            public static readonly int clusterCullingReflectionProbeParamsBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParamsBuffer");

            /// <summary>
            /// 供 compute shader 使用的反射探针数据缓冲。
            /// 值：<c>_ClusterCullingReflectionProbeDatas4CSBuffer</c>。
            /// </summary>
            public static readonly int reflectionProbeDatas4CSBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeDatas4CSBuffer");
        }

        // ── 簇剔除数据结构（自旧版 ClusterCullingReflectionProbePass 迁移）──
    }

    /// <summary>
    /// 供 compute shader 剔除的单探针数据。
    /// 每个元素保存单个反射探针的世界空间包围盒中心与尺寸。
    /// </summary>
    [Serializable]
    public struct ReflectionProbeData4CS
    {
        /// <summary>
        /// 反射探针世界空间包围盒中心。
        /// </summary>
        public float3 boundCenter;

        /// <summary>
        /// 反射探针世界空间包围盒尺寸。
        /// </summary>
        public float3 boundExtents;
    }

    /// <summary>
    /// 剔除后传给 shader 的单探针渲染数据。
    /// 与旧版 <c>ClusterCullingReflectionProbeDatas</c> 结构保持一致。
    /// </summary>
    [Serializable]
    unsafe public struct ClusterCullingReflectionProbeDatas
    {
        /// <summary>
        /// 探针世界空间包围盒最大角点。
        /// </summary>
        public Vector3 boxMax;

        /// <summary>
        /// 探针间交叉淡化的混合距离。
        /// </summary>
        public float blendDistance;

        /// <summary>
        /// 探针世界空间包围盒最小角点。
        /// </summary>
        public Vector3 boxMin;

        /// <summary>
        /// 本探针的重要性权重。
        /// </summary>
        public float importance;

        /// <summary>
        /// 反射探针的世界空间位置。
        /// </summary>
        public Vector3 positionWS;

        /// <summary>
        /// 本探针贡献的强度倍率。
        /// </summary>
        public float intensity;

        /// <summary>
        /// 采样探针 cubemap 使用的缩放与偏移。
        /// </summary>
        public Vector4 scaleOffset;

        /// <summary>
        /// 当前探针 cubemap 的 mip 数量。不同分辨率的探针 cubemap
        /// 拥有不同的 mip 数量。
        /// </summary>
        public float mipCount;

        public Vector3 unused;
    }

    /// <summary>
    /// 传给 compute shader 的簇剔除参数。
    /// 与旧版 <c>ClusterCullingReflectionProbeParams</c> 结构保持一致。
    /// </summary>
    [Serializable]
    unsafe public struct ClusterCullingReflectionProbeParams
    {
        /// <summary>
        /// 屏幕空间簇尺寸（XY）。
        /// </summary>
        public Vector2 clusterSizeXY;

        /// <summary>
        /// 簇深度切片的 Z 轴缩放与偏移。
        /// </summary>
        public Vector2 clusterZScaleOffset;

        /// <summary>
        /// 掩码缓冲中每簇的 32 位字数。
        /// </summary>
        public int wordsPerCluster;

        /// <summary>
        /// 反射探针总数。
        /// </summary>
        public int reflectionProbeCount;

        /// <summary>
        /// 未用填充（字段 0）。
        /// </summary>
        public float unused0;

        /// <summary>
        /// 未用填充（字段 1）。
        /// </summary>
        public float unused1;
    }
}
