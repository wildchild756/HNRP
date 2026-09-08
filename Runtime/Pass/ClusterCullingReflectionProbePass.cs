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
    /// <summary>
    /// Runs cluster-based reflection probe culling via compute shader and outputs
    /// a reflection probe atlas, a mask buffer, and a probe data buffer for
    /// downstream passes (e.g. forward / deferred shading).
    /// </summary>
    /// <remarks>
    /// <para><b>New Pass system</b> (ADR-002, ADR-011):
    /// Inherits from <see cref="Pass"/> instead of the legacy <see cref="PassBase"/>.
    /// Uses name-based <see cref="TextureSlot"/> and <see cref="ComputeBufferSlot"/>
    /// outputs for downstream connections instead of index-based slot registration.
    /// </para>
    /// <para>
    /// The compute shader is accessed via
    /// <see cref="CameraContext.RuntimeResources"/>.<see cref="HNRenderPipelineRuntimeResources.clusterCullingReflectionProbeCS"/>.
    /// </para>
    /// </remarks>
    [Pass("Cluster Culling Probe")]
    public sealed class ClusterCullingReflectionProbePass : Pass
    {
        // ── Configurable parameters ──

        /// <summary>
        /// Parameters for the reflection probe atlas allocated when the
        /// <see cref="ReflectionProbeAtlasInputSlot"/> input is not connected
        /// / valid. Default: HDR 4096 atlas with trilinear mip filtering.
        /// </summary>
        [SerializeField]
        private TextureResourceParams m_AtlasParams;

        /// <summary>
        /// Gets or sets the reflection probe atlas allocation parameters.
        /// </summary>
        public TextureResourceParams AtlasParams
        {
            get => m_AtlasParams;
            set => m_AtlasParams = value;
        }

        // ── Slots ──

        /// <summary>
        /// Gets the input texture slot for the reflection probe atlas
        /// (<see cref="TextureSlot"/>, <see cref="SlotDirection.Input"/>).
        /// When connected with a valid handle, this pass writes the blitted
        /// octahedral data into the upstream atlas; otherwise it allocates its
        /// own atlas from <see cref="AtlasParams"/>. The result is exposed via
        /// <see cref="ReflectionProbeAtlasOutputSlot"/> for downstream passes.
        /// </summary>
        public TextureSlot? ReflectionProbeAtlasInputSlot { get; private set; }

        /// <summary>
        /// Gets the output texture slot for the reflection probe atlas
        /// (<see cref="TextureSlot"/>, <see cref="SlotDirection.Output"/>).
        /// Pass-through of the input atlas after writing, so downstream
        /// passes can connect without a separate resource node.
        /// </summary>
        public TextureSlot? ReflectionProbeAtlasOutputSlot { get; private set; }

        /// <summary>
        /// Gets the output compute buffer slot for the cluster culling
        /// reflection probe mask buffer
        /// (<see cref="ComputeBufferSlot"/>, <see cref="SlotDirection.Output"/>).
        /// </summary>
        public ComputeBufferSlot? ClusterCullingReflectionProbeMaskBufferSlot { get; private set; }

        /// <summary>
        /// Gets the output compute buffer slot for the cluster culling
        /// reflection probe data buffer
        /// (<see cref="ComputeBufferSlot"/>, <see cref="SlotDirection.Output"/>).
        /// </summary>
        public ComputeBufferSlot? ClusterCullingReflectionProbeDatasBufferSlot { get; private set; }

        /// <summary>
        /// Gets or sets the dictionary of realtime probe cubemap textures rendered
        /// this frame, keyed by probe instance id. Set by the pipeline after
        /// Phase B (realtime probe rendering) completes. When set, the pass uses
        /// these textures instead of reading <c>probe.realtimeTexture</c> directly.
        /// </summary>
        public IReadOnlyDictionary<int, Texture> RenderedProbeTextures { get; set; }

        // ── Camera context ──

        private CameraContext? m_Context;
        private ComputeShader? m_ComputeShader;

        // ── Reusable scratch buffers (zero per-frame GC) ──
        // The render loop fills these pre-allocated buffers every frame instead of
        // allocating new arrays / lists. Lazy-initialized once, reused forever.

        private List<ProbeEntry> m_ProbeEntries;
        private ReflectionProbeData4CS[] m_CullingDatas;
        private ClusterCullingReflectionProbeDatas[] m_SampleDatas;
        private int4[] m_ScaleOffsetsInt;
        private Vector4[] m_ScaleOffsetsUV;
        private Texture[] m_ProbeTextures;

        // ── Constants (mirrored from legacy ClusterCullingReflectionProbePass) ──

        private const int MaxReflectionProbesOnScreen = 64;
        private const int ReflectionProbeAtlasSize = 4096;
        private const int ReflectionProbeAtlasMipCount = 7;
        private const int ReflectionProbeAtlasTexelPadding = 2;
        private const int AtlasResolutionLevels = 5;
        private const uint MaxOffsetMask = 1u << 25;
        private const int MaxClusterMaskWords = 4096 * 4;
        private const string ClusterCullingKernelName = "ClusterCullingReflectionProbeCS";

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterCullingReflectionProbePass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public ClusterCullingReflectionProbePass()
        {
            m_AtlasParams = CreateDefaultAtlasParams();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterCullingReflectionProbePass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public ClusterCullingReflectionProbePass(string passName)
            : base(passName)
        {
            m_AtlasParams = CreateDefaultAtlasParams();
        }

        /// <inheritdoc />
        public override void CopyFrom(Pass source)
        {
            if (source is ClusterCullingReflectionProbePass s)
            {
                m_AtlasParams = s.m_AtlasParams;
            }

            // RenderedProbeTextures is per-frame runtime state set by the
            // pipeline, never copied.
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

        // ── Lifecycle ──

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
        /// Stores the camera context and resolves the cluster culling compute
        /// shader from <see cref="CameraContext.RuntimeResources"/>.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            m_Context = context;

            if (context.RuntimeResources != null)
            {
                m_ComputeShader = context.RuntimeResources.clusterCullingReflectionProbeCS;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Creates the reflection probe atlas (texture), mask buffer, and two probe
        /// data buffers as render graph resources. Records a render function that:
        /// <list type="bullet">
        ///   <item>uploads the visible realtime probe data (culling bounds + sample data)</item>
        ///   <item>dispatches the cluster culling compute shader to populate the mask buffer</item>
        ///   <item>blits every realtime probe cubemap into its octahedral atlas region</item>
        ///   <item>generates the atlas mip chain</item>
        /// </list>
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            if (m_ComputeShader == null)
            {
                Debug.LogError(
                    "Cluster Culling Reflection Probe Compute Shader is null. " +
                    "Ensure HNRenderPipelineRuntimeResources is assigned in the pipeline asset.");
                IsEnabled &= false;
            }

            if (m_Context == null)
            {
                Debug.LogError("CameraContext is null. Initialize must be called before Record.");
                IsEnabled &= false;
            }

            // ── Collect visible probes (baked + realtime) and pack them into the
            // atlas using the legacy recursive-quad layout ──
            // Visible baked probes contribute their baked cubemap; realtime probes
            // (rendered in Phase B before main cameras) contribute their realtime
            // cubemap. Every visible probe is blitted every frame because the atlas
            // is a transient render graph resource.
            EnsureScratchBuffers();
            List<ProbeEntry> entries = m_ProbeEntries;
            entries.Clear();
            if (m_Context.VisibleReflectionProbes.IsCreated)
            {
                var visibleProbes = m_Context.VisibleReflectionProbes;
                for (int i = 0; i < visibleProbes.Length; i++)
                {
                    ReflectionProbe probe = ReflectionProbeRenderUtils.GetReflectionProbe(visibleProbes[i]);
                    if (probe == null)
                    {
                        continue;
                    }

                    // Bake mode uses the probe's baked/custom cubemap; realtime mode
                    // uses the probe's persistent realtime cubemap. Time-slicing only
                    // controls when the cubemap is re-rendered, not whether the atlas
                    // includes the probe, so non-refresh frames still use the last
                    // rendered cubemap instead of dropping the probe.
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

            // ── Assign atlas regions (legacy recursive-quad layout) ──
            // Larger resolutions claim their region first; offsetMask encodes the
            // recursive subdivision path (see GetOffset).
            ReflectionProbeData4CS[] cullingDatas = m_CullingDatas;
            ClusterCullingReflectionProbeDatas[] sampleDatas = m_SampleDatas;
            int4[] scaleOffsetsInt = m_ScaleOffsetsInt;
            Vector4[] scaleOffsetsUV = m_ScaleOffsetsUV;
            Texture[] probeTextures = m_ProbeTextures;

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

                    cullingDatas[probeIndex] = new ReflectionProbeData4CS
                    {
                        boundCenter = bounds.center,
                        boundExtents = bounds.extents,
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

            using (var builder = renderGraph.AddRenderPass<ClusterCullingReflectionProbePassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                // ── Input/Output: reflection probe atlas ──
                // Consume a connected input atlas when its handle is valid;
                // otherwise allocate the atlas locally from AtlasParams. The
                // blitted octahedral data is written into it, then exposed for
                // downstream passes.

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
                        m_AtlasParams.CreateDesc("Reflection Probe Atlas", m_Context.Camera));
                }

                if (!atlasHandle.IsValid())
                {
                    return;
                }

                passData.reflectionProbeAtlas = builder.WriteTexture(atlasHandle);

                // Pass-through to output slot for downstream passes.
                if (ReflectionProbeAtlasOutputSlot != null)
                {
                    ReflectionProbeAtlasOutputSlot.SetHandle(atlasHandle);
                }

                // ── Output: mask buffer ──

                ComputeBufferHandle maskHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxClusterMaskWords,
                        sizeof(uint))
                    { name = "Cluster Culling Reflection Probe Mask Buffer" });

                passData.clusterCullingReflectionProbeMaskBuffer = builder.WriteComputeBuffer(maskHandle);

                // ── Output: culling data buffer (ReflectionProbeData4CS layout) ──

                ComputeBufferHandle cullingDatasHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxReflectionProbesOnScreen,
                        UnsafeUtility.SizeOf<ReflectionProbeData4CS>())
                    { name = "Cluster Culling Reflection Probe Culling Datas Buffer" });

                passData.cullingDatasBuffer = builder.WriteComputeBuffer(cullingDatasHandle);

                // ── Output: sample data buffer (ClusterCullingReflectionProbeDatas layout) ──

                ComputeBufferHandle sampleDatasHandle = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MaxReflectionProbesOnScreen,
                        UnsafeUtility.SizeOf<ClusterCullingReflectionProbeDatas>())
                    { name = "Cluster Culling Reflection Probe Datas Buffer" });

                passData.sampleDatasBuffer = builder.WriteComputeBuffer(sampleDatasHandle);

                // ── Publish real render graph handles to output slots ──

                ClusterCullingReflectionProbeMaskBufferSlot!.SetHandle(maskHandle);
                ClusterCullingReflectionProbeDatasBufferSlot!.SetHandle(sampleDatasHandle);

                // ── Compute shader setup ──

                passData.clusterCullingReflectionProbeCS = m_ComputeShader;
                passData.clusterCullingKernel = m_ComputeShader.FindKernel(ClusterCullingKernelName);

                Camera camera = m_Context.Camera;
                int2 screenResolution = math.int2(camera.pixelWidth, camera.pixelHeight);
                int3 clusterSize = GetClusterSize(screenResolution);
                float2 clusterZScaleOffset = GetClusterZScaleOffset(
                    clusterSize, camera.orthographic,
                    camera.nearClipPlane, camera.farClipPlane);

                int itemsPerCluster = MaxReflectionProbesOnScreen;
                int wordsPerCluster = (itemsPerCluster + 31) / 32 + 1;

                // The compute shader transforms cluster slice depths through the GPU
                // projection (D3D-style z in [0,1]) to clip space, then to world space
                // to test AABB overlap against probe bounds. Using the raw OpenGL
                // projectionMatrix (NDC z in [-1,1]) would put half the clip z range
                // below the shader's [0,1] clamp and break overlap tests.
                // All passes in HNRP render through RenderGraph which always renders
                // to render textures internally, so renderIntoTexture is always true.
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(
                    camera.projectionMatrix, true);
                Matrix4x4 clipToView = gpuProj.inverse;
                Matrix4x4 viewToClip = gpuProj;
                Matrix4x4 clipToWorld = (camera.worldToCameraMatrix * gpuProj).inverse;

                // ── Per-frame params (uploaded to shaders via PushGlobal) ──

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

                // ── Store per-frame values on the (pooled) pass data so the
                // render function closure only captures `this` (zero allocation) ──

                passData.clusterSize = clusterSize;
                passData.probeCount = probeCount;
                passData.cameraOrthographic = camera.orthographic;
                passData.clipToView = clipToView;
                passData.viewToClip = viewToClip;
                passData.clipToWorld = clipToWorld;

                // ── Render function ──

                builder.SetRenderFunc(
                    (ClusterCullingReflectionProbePassData data, RenderGraphContext ctx) =>
                    {
                        if(!IsEnabled)
                        {
                            return;
                        }

                        // Upload probe data for the culling dispatch and the shader.
                        ctx.cmd.SetBufferData(data.cullingDatasBuffer, m_CullingDatas);
                        ctx.cmd.SetBufferData(data.sampleDatasBuffer, m_SampleDatas);

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

                        // Dispatch one thread per cluster over the full 3D grid.
                        // numthreads(8,8,1): thread groups cover x/y, thread groups
                        // along z cover every depth slice (id.z = cluster index z).
                        int threadGroupX = (clusterSizeInPass.x + 7) / 8;
                        int threadGroupY = (clusterSizeInPass.y + 7) / 8;
                        ctx.cmd.DispatchCompute(
                            data.clusterCullingReflectionProbeCS,
                            data.clusterCullingKernel,
                            threadGroupX,
                            threadGroupY,
                            clusterSizeInPass.z);

                        // ── Blit every visible probe cubemap into its atlas region ──
                        // The octahedral projection is drawn into the region's mip 0
                        // (padding adjusted), then GenerateMips builds the remaining
                        // atlas mip chain. This avoids depending on the source cubemap
                        // having valid mips (realtime cubemaps may not).
                        for (int i = 0; i < probeCountInPass; i++)
                        {
                            Texture source = m_ProbeTextures[i];
                            if (source == null)
                            {
                                continue;
                            }

                            int texelPadding = ReflectionProbeAtlasTexelPadding;
                            Vector2 textureSizeWithoutPadding =
                                GetTextureSizeWithoutPadding(m_ScaleOffsetsUV[i], texelPadding);

                            ctx.cmd.SetRenderTarget(
                                (RenderTargetIdentifier)data.reflectionProbeAtlas);
                            var propertyBlock = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                            Blitter.BlitCubeToOctahedral2DQuadWithPadding(
                                ctx.cmd,
                                propertyBlock,
                                source,
                                textureSizeWithoutPadding,
                                m_ScaleOffsetsUV[i],
                                0,
                                true,
                                texelPadding);
                        }

                        if (probeCountInPass > 0)
                        {
                            ctx.cmd.GenerateMips((RenderTexture)data.reflectionProbeAtlas);
                        }

                        // Upload cluster culling params so fragment shaders can
                        // resolve cluster indices for probe iteration.
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
            // No disposable resources held by this pass.
            m_ComputeShader = null;
            m_Context = null;
        }

        // ── Scratch buffer helpers ──

        /// <summary>
        /// Lazily allocates the per-frame scratch buffers once and reuses them
        /// forever, keeping the render loop allocation-free.
        /// </summary>
        private void EnsureScratchBuffers()
        {
            if (m_ProbeEntries == null)
            {
                m_ProbeEntries = new List<ProbeEntry>(MaxReflectionProbesOnScreen);
                m_CullingDatas = new ReflectionProbeData4CS[MaxReflectionProbesOnScreen];
                m_SampleDatas = new ClusterCullingReflectionProbeDatas[MaxReflectionProbesOnScreen];
                m_ScaleOffsetsInt = new int4[MaxReflectionProbesOnScreen];
                m_ScaleOffsetsUV = new Vector4[MaxReflectionProbesOnScreen];
                m_ProbeTextures = new Texture[MaxReflectionProbesOnScreen];
            }
        }

        // ── Atlas layout helpers (legacy recursive-quad layout) ──

        /// <summary>
        /// Maps a probe resolution to an atlas level: <c>0..4</c> maps
        /// <c>4096..256</c> texels per probe. Unsupported resolutions return <c>-1</c>.
        /// </summary>
        private static int AtlasLevelForResolution(int resolution)
        {
            int log2 = (int)(Mathf.Log(resolution, 2) + 0.5f);
            int level = 11 - log2;
            return level >= 0 && level < AtlasResolutionLevels ? level : -1;
        }

        /// <summary>
        /// Computes the atlas region offset for a probe from the recursive-quad
        /// <paramref name="offsetMask"/>.
        /// The mask stores the subdivision path bitwise: the effective bits are the
        /// middle 2 * 5 = 10 bits (15..24); adjacent bit pairs (low = x, high = y)
        /// encode the recursive quarter split, supporting probe resolutions from
        /// 4096 down to 256.
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
        /// Converts an integer atlas region (size + offset in texels) to a normalized
        /// scale/bias vector without padding — the value stored in the datas buffer
        /// and consumed by <c>GetReflectionProbeAtlasUV</c>.
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
        /// Computes the source texture size (in texels) excluding padding for a
        /// normalized atlas region.
        /// </summary>
        private static Vector2 GetTextureSizeWithoutPadding(Vector4 scaleOffset, int texelPadding)
        {
            float scaleX = scaleOffset.x * ReflectionProbeAtlasSize - texelPadding * 2;
            float scaleY = scaleOffset.y * ReflectionProbeAtlasSize - texelPadding * 2;
            return new Vector2(scaleX, scaleY);
        }

        /// <summary>
        /// A visible probe scheduled for atlas packing this frame.
        /// </summary>
        private struct ProbeEntry
        {
            public ReflectionProbe Probe;
            public Texture Texture;
            public int Level;
        }

        // ── Cluster size computation ──

        private const int ClusterMinTileSize = 8;
        private const int ClusterMaxZSlice = 128;
        private const int ClusterMinZSlice = 16;

        private static int3 GetClusterSize(int2 screenResolution)
        {
            // Each cluster stores wordsPerCluster uints in the mask buffer
            // (header + one word per 32 probe bits). The slice count must be
            // derived from the mask buffer capacity divided by words per
            // cluster, otherwise the compute shader writes past the buffer end.
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
        /// Render graph pass data container for
        /// <see cref="ClusterCullingReflectionProbePass"/>.
        /// </summary>
        private sealed class ClusterCullingReflectionProbePassData
        {
            /// <summary>
            /// The reflection probe atlas texture handle.
            /// </summary>
            public TextureHandle reflectionProbeAtlas;

            /// <summary>
            /// The cluster culling mask buffer handle.
            /// </summary>
            public ComputeBufferHandle clusterCullingReflectionProbeMaskBuffer;

            /// <summary>
            /// The cluster culling probe culling data buffer handle
            /// (<see cref="ReflectionProbeData4CS"/> layout, fed to the compute shader).
            /// </summary>
            public ComputeBufferHandle cullingDatasBuffer;

            /// <summary>
            /// The cluster culling probe sample data buffer handle
            /// (<see cref="ClusterCullingReflectionProbeDatas"/> layout, consumed by shaders).
            /// </summary>
            public ComputeBufferHandle sampleDatasBuffer;

            /// <summary>
            /// The cluster culling compute shader.
            /// </summary>
            public ComputeShader clusterCullingReflectionProbeCS;

            /// <summary>
            /// The kernel index for the culling dispatch.
            /// </summary>
            public int clusterCullingKernel;

            /// <summary>
            /// The cluster culling parameters uploaded to shaders via
            /// <c>_ClusterCullingReflectionProbeParamsBuffer</c>.
            /// </summary>
            public ClusterCullingReflectionProbeParams clusterCullingReflectionProbeParams;

            /// <summary>
            /// The cluster grid dimensions for this frame.
            /// </summary>
            public int3 clusterSize;

            /// <summary>
            /// The number of probes packed into the atlas this frame.
            /// </summary>
            public int probeCount;

            /// <summary>
            /// Whether the current camera is orthographic.
            /// </summary>
            public bool cameraOrthographic;

            /// <summary>
            /// The clip-to-view matrix.
            /// </summary>
            public Matrix4x4 clipToView;

            /// <summary>
            /// The view-to-clip matrix.
            /// </summary>
            public Matrix4x4 viewToClip;

            /// <summary>
            /// The clip-to-world matrix.
            /// </summary>
            public Matrix4x4 clipToWorld;
        }

        // ── Property IDs ──

        /// <summary>
        /// Shader property identifiers for cluster culling reflection probe
        /// compute shader parameters.
        /// Mirrors the shader property IDs used by the cluster culling
        /// reflection probe compute shader.
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// Reflection probe atlas texture.
            /// Value: <c>_ReflectionProbeAtlas</c>.
            /// </summary>
            public static readonly int reflectionProbeAtlas =
                Shader.PropertyToID("_ReflectionProbeAtlas");

            /// <summary>
            /// Cluster culling reflection probe mask buffer (RWStructuredBuffer).
            /// Value: <c>_ClusterCullingReflectionProbeMaskBuffer</c>.
            /// </summary>
            public static readonly int clusterCullingReflectionProbeMaskBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeMaskBuffer");

            /// <summary>
            /// Cluster culling reflection probe data buffer (RWStructuredBuffer).
            /// Value: <c>_ClusterCullingReflectionProbeDatasBuffer</c>.
            /// </summary>
            public static readonly int clusterCullingReflectionProbeDatasBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeDatasBuffer");

            /// <summary>
            /// Culling params 0: x=z scale, y=z offset, z=wordsPerCluster, w=isOrthographic.
            /// Value: <c>_ClusterCullingReflectionProbeParams0</c>.
            /// </summary>
            public static readonly int cullingParams0 =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParams0");

            /// <summary>
            /// Culling params 1: xyz=clusterSize, w=probeCount.
            /// Value: <c>_ClusterCullingReflectionProbeParams1</c>.
            /// </summary>
            public static readonly int cullingParams1 =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParams1");

            /// <summary>
            /// Clip-to-view matrix.
            /// Value: <c>_ClusterCullingReflectionProbeClipToView</c>.
            /// </summary>
            public static readonly int cullingClipToViewMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeClipToView");

            /// <summary>
            /// View-to-clip matrix.
            /// Value: <c>_ClusterCullingReflectionProbeViewToClip</c>.
            /// </summary>
            public static readonly int cullingViewToClipMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeViewToClip");

            /// <summary>
            /// Clip-to-world matrix.
            /// Value: <c>_ClusterCullingReflectionProbeClipToWorld</c>.
            /// </summary>
            public static readonly int cullingClipToWorldMatrix =
                Shader.PropertyToID("_ClusterCullingReflectionProbeClipToWorld");

            /// <summary>
            /// Cluster culling reflection probe params buffer (structured buffer for probe parameters).
            /// Value: <c>_ClusterCullingReflectionProbeParamsBuffer</c>.
            /// </summary>
            public static readonly int clusterCullingReflectionProbeParamsBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeParamsBuffer");

            /// <summary>
            /// Reflection probe data for compute shader buffer.
            /// Value: <c>_ClusterCullingReflectionProbeDatas4CSBuffer</c>.
            /// </summary>
            public static readonly int reflectionProbeDatas4CSBuffer =
                Shader.PropertyToID("_ClusterCullingReflectionProbeDatas4CSBuffer");
        }

        // ── Cluster culling data structures (moved from legacy ClusterCullingReflectionProbePass) ──
    }

    /// <summary>
    /// Per-probe data for compute shader culling.
    /// Each element holds the world-space bound center and extents for a single reflection probe.
    /// </summary>
    [Serializable]
    public struct ReflectionProbeData4CS
    {
        /// <summary>
        /// The world-space bound center of the reflection probe.
        /// </summary>
        public float3 boundCenter;

        /// <summary>
        /// The world-space bound extents of the reflection probe.
        /// </summary>
        public float3 boundExtents;
    }

    /// <summary>
    /// Per-probe rendering data passed to the shader after culling.
    /// Mirrors the legacy <c>ClusterCullingReflectionProbeDatas</c> struct.
    /// </summary>
    [Serializable]
    unsafe public struct ClusterCullingReflectionProbeDatas
    {
        /// <summary>
        /// The maximum corner of the probe bounding box in world space.
        /// </summary>
        public Vector3 boxMax;

        /// <summary>
        /// The blend distance for cross-fading between probes.
        /// </summary>
        public float blendDistance;

        /// <summary>
        /// The minimum corner of the probe bounding box in world space.
        /// </summary>
        public Vector3 boxMin;

        /// <summary>
        /// The importance weight of this probe.
        /// </summary>
        public float importance;

        /// <summary>
        /// The world-space position of the reflection probe.
        /// </summary>
        public Vector3 positionWS;

        /// <summary>
        /// The intensity multiplier for this probe's contribution.
        /// </summary>
        public float intensity;

        /// <summary>
        /// The scale and offset for sampling the probe cubemap.
        /// </summary>
        public Vector4 scaleOffset;

        /// <summary>
        /// The mip count of current probe cubemap. The probe cubemap with different resolution has different mip count.
        /// </summary>
        public float mipCount;

        public Vector3 unused;
    }

    /// <summary>
    /// Cluster culling parameters passed to the compute shader.
    /// Mirrors the legacy <c>ClusterCullingReflectionProbeParams</c> struct.
    /// </summary>
    [Serializable]
    unsafe public struct ClusterCullingReflectionProbeParams
    {
        /// <summary>
        /// The cluster dimensions in screen space (XY).
        /// </summary>
        public Vector2 clusterSizeXY;

        /// <summary>
        /// The cluster Z scale and offset for depth slicing.
        /// </summary>
        public Vector2 clusterZScaleOffset;

        /// <summary>
        /// The number of 32-bit words per cluster in the mask buffer.
        /// </summary>
        public int wordsPerCluster;

        /// <summary>
        /// The total number of reflection probes.
        /// </summary>
        public int reflectionProbeCount;

        /// <summary>
        /// Unused padding (field 0).
        /// </summary>
        public float unused0;

        /// <summary>
        /// Unused padding (field 1).
        /// </summary>
        public float unused1;
    }
}
