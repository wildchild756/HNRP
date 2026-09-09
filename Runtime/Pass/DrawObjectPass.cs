// <copyright file="DrawObjectPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// Generic parameterized object-drawing pass.
    /// Color / depth targets and the renderer list are consumed from connected
    /// input slots when available; otherwise the pass allocates them itself from
    /// its own <see cref="TextureResourceParams"/> / <see cref="RendererListParams"/>
    /// parameters. Lighting / probe data (light datas, reflection probe atlas,
    /// cluster culling masks) are optional read-only inputs.
    /// </summary>
    /// <remarks>
    /// <para><b>Inputs (all optional):</b></para>
    /// <list type="bullet">
    ///   <item><b>ColorTarget</b> — the color buffer written by draw calls.</item>
    ///   <item><b>DepthTarget</b> — the depth buffer written by draw calls.</item>
    ///   <item><b>LightDatas</b> — compute buffer with light data for shader access.</item>
    ///   <item><b>ReflectionProbeAtlas</b> — reflection probe cubemap atlas texture.</item>
    ///   <item><b>ProbeMask</b> — cluster culling reflection probe mask buffer.</item>
    ///   <item><b>ProbeDatas</b> — cluster culling reflection probe data buffer.</item>
    ///   <item><b>LightMask</b> — cluster culling light mask buffer.</item>
    ///   <item><b>RendererList</b> — the renderer list to draw.</item>
    /// </list>
    /// <para>
    /// When a required input slot is <b>not connected</b> or its handle is
    /// <b>not valid</b>, the pass creates the resource internally (color / depth
    /// buffers from <see cref="ColorTargetParams"/> / <see cref="DepthTargetParams"/>,
    /// the renderer list from <see cref="RendererListParams"/>). This makes the
    /// pass usable as a chain head without external resource nodes.
    /// </para>
    /// <para>
    /// When <see cref="SetLightGlobals"/> is <c>true</c> the render function also
    /// binds the probe / light / light-data shader globals (replacing the old
    /// Forward Opaque pass behavior). When <c>false</c> only
    /// <c>DrawRendererList</c> is emitted (e.g. preview graphs without cluster data).
    /// </para>
    /// <para>
    /// <b>Outputs (pass-through for downstream chaining):</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><b>ColorTargetOutput</b> — pass-through of the resolved color target
    ///   (input or self-allocated) so downstream passes can chain.</item>
    ///   <item><b>DepthTargetOutput</b> — pass-through of the resolved depth target.</item>
    /// </list>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class DrawObjectPass : Pass
    {
        /// <summary>
        /// The constant pass name string used for registration and identification.
        /// </summary>
        public const string PassNameConst = "Draw Object";

        // ── Configurable parameters ──

        /// <summary>
        /// Parameters for the color target allocated when the
        /// <see cref="ColorTargetSlot"/> input is not connected / valid.
        /// Default: full-resolution LDR.
        /// </summary>
        [SerializeField]
        private TextureResourceParams colorTargetParams;

        /// <summary>
        /// Parameters for the depth target allocated when the
        /// <see cref="DepthTargetSlot"/> input is not connected / valid.
        /// Default: full-resolution 32-bit depth.
        /// </summary>
        [SerializeField]
        private TextureResourceParams depthTargetParams;

        /// <summary>
        /// Parameters for the renderer list created when the
        /// Default: opaque queue, layer mask <c>0x00000001</c>.
        /// </summary>
        [SerializeField]
        private RendererListParams rendererListParams;

        /// <summary>
        /// Whether the render function should set the probe / light / light-datas
        /// shader globals before drawing. Default <c>true</c>.
        /// </summary>
        [SerializeField]
        private bool setLightGlobals = true;

        /// <summary>
        /// Whether the objects should receive shadow.
        /// </summary>
        [SerializeField]
        private bool receiveShadow = true;

        /// <summary>
        /// Gets or sets the color target allocation parameters.
        /// </summary>
        public TextureResourceParams ColorTargetParams
        {
            get => colorTargetParams;
            set => colorTargetParams = value;
        }

        /// <summary>
        /// Gets or sets the depth target allocation parameters.
        /// </summary>
        public TextureResourceParams DepthTargetParams
        {
            get => depthTargetParams;
            set => depthTargetParams = value;
        }

        /// <summary>
        /// Gets or sets the renderer list allocation parameters.
        /// </summary>
        public RendererListParams RendererListParams
        {
            get => rendererListParams;
            set => rendererListParams = value;
        }

        /// <summary>
        /// Gets or sets the rendering layer mask used when the renderer list is
        /// allocated locally (via <see cref="RendererListParams"/>).
        /// Default is <c>0x00000001</c> (layer 0).
        /// </summary>
        public uint RenderingLayerMask
        {
            get => rendererListParams.RenderingLayerMask;
            set => rendererListParams.RenderingLayerMask = value;
        }

        // /// <summary>
        // /// Gets or sets a value indicating whether the render function should set
        // /// the probe / light / light-datas shader globals before drawing.
        // /// Default is <c>true</c>. Set to <c>false</c> for graphs that have no
        // /// cluster culling data (e.g. preview).
        // /// </summary>
        // public bool SetLightGlobals
        // {
        //     get => setLightGlobals;
        //     set => setLightGlobals = value;
        // }

        /// <summary>
        /// Gets or sets a value indicating whether the objects should receive shadow.
        /// Default is <c>true</c>.
        /// </summary>
        public bool ReceiveShadow
        {
            get => receiveShadow;
            set => receiveShadow = value;
        }

        // ── Slots ──

        /// <summary>
        /// Gets the input color target slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetSlot { get; private set; }

        /// <summary>
        /// Gets the input depth target slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? DepthTargetSlot { get; private set; }

        /// <summary>
        /// Gets the cascade shadow map slot.
        /// </summary>
        public TextureSlot? CascadeShadowMapSlot { get; private set; }

        /// <summary>
        /// Gets the screen space shadow map slot.
        /// </summary>
        public TextureSlot? ScreenSpaceShadowMapSlot { get; private set; }

        /// <summary>
        /// Gets the input light data compute buffer slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public ComputeBufferSlot? LightDatasSlot { get; private set; }

        /// <summary>
        /// Gets the input reflection probe atlas texture slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ReflectionProbeAtlasSlot { get; private set; }

        /// <summary>
        /// Gets the input cluster culling reflection probe mask buffer slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public ComputeBufferSlot? ProbeMaskSlot { get; private set; }

        /// <summary>
        /// Gets the input cluster culling reflection probe data buffer slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public ComputeBufferSlot? ProbeDatasSlot { get; private set; }

        /// <summary>
        /// Gets the input cluster culling light mask buffer slot.
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public ComputeBufferSlot? LightMaskSlot { get; private set; }

        /// <summary>
        /// Gets the output color target slot (pass-through of the resolved
        /// <see cref="ColorTargetSlot"/> handle for downstream chaining).
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetOutputSlot { get; private set; }

        /// <summary>
        /// Gets the output depth target slot (pass-through of the resolved
        /// <see cref="DepthTargetSlot"/> handle for downstream chaining).
        /// Available after <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? DepthTargetOutputSlot { get; private set; }

        // ── Camera context ──

        private CameraContext? cameraContext;

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawObjectPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public DrawObjectPass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawObjectPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public DrawObjectPass(string passName)
            : base(passName)
        {
        }

        /// <inheritdoc />
        public override void CopyFrom(Pass source)
        {
            if (source is DrawObjectPass s)
            {
                colorTargetParams = s.colorTargetParams;
                depthTargetParams = s.depthTargetParams;
                rendererListParams = s.rendererListParams;
                // setLightGlobals = s.setLightGlobals;
            }
        }

        // ── Lifecycle ──

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
        /// Stores the camera context so renderer list / lighting globals can be
        /// resolved during <see cref="Record"/>.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;

            colorTargetParams = TextureResourceParams.CreateDefault();
            var format = template.Settings.AllowHDR ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            colorTargetParams.ColorFormat = format;

            depthTargetParams = TextureResourceParams.CreateDefault();
            depthTargetParams.DepthBits = UnityEngine.Rendering.DepthBits.Depth32;

            rendererListParams = RendererListParams.CreateDefault();
        }

        /// <inheritdoc />
        public override void Record(RenderGraph renderGraph)
        {
            if (ColorTargetSlot == null || DepthTargetSlot == null)
            {
                IsEnabled &= false;
            }

            if (cameraContext == null)
            {
                IsEnabled &= false;
            }

            Camera camera = cameraContext.Camera;
            if (camera == null)
            {
                IsEnabled &= false;
            }

            // ── Required inputs: color / depth targets + renderer list ──
            // Consume a connected input when its handle is valid; otherwise
            // allocate the resource locally from this pass's parameters.

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

            if (!colorTarget.IsValid() || !depthTarget.IsValid())
            {
                IsEnabled &= false;
            }

            RendererListHandle rendererList = CreateRendererList(renderGraph);

            if (!rendererList.IsValid())
            {
                IsEnabled &= false;
            }

            // Pass-through the resolved color / depth handles to the output slots
            // so downstream passes can chain from this pass's outputs.
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

            // ── Optional inputs: gated independently on connectivity ──

            bool hasCascadeShadow = false;
            bool isScreenSpaceShadow = false;
            if(CascadeShadowMapSlot.IsConnected && CascadeShadowMapSlot.HasHandle)
            {
                hasCascadeShadow = true;
                if(ScreenSpaceShadowMapSlot.IsConnected && ScreenSpaceShadowMapSlot.HasHandle)
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

            // ── Renderer list: read from the resolved handle ──

            passData.rendererList = builder.UseRendererList(rendererList);

            // ── Render function ──
            // The probe keyword requires all three probe slots connected at record
            // time (mirrors the original Forward Opaque behavior). Per-frame flags
            // are stored on the pooled pass data so the render function closure
            // only captures `this` (zero allocation).

            // bool setLightGlobals = this.setLightGlobals;
            bool enableProbeKeyword = hasReflectionProbeAtlas && hasProbeMask && hasProbeDatas;

            // passData.setLightGlobals = setLightGlobals;
            passData.enableProbeKeyword = enableProbeKeyword;
            passData.hasLightMask = hasLightMask;
            passData.hasLightDatas = hasLightDatas;

            // Explicit camera matrices for this pass. SetupCameraProperties on the
            // ScriptableRenderContext only stores the LAST camera's matrix as the
            // active global state, so passes that render offscreen cameras (e.g.
            // realtime probe faces) must set the matrices per pass — otherwise every
            // draw would use the main camera's view.
            // All passes in HNRP render through RenderGraph which always renders to
            // render textures internally, so renderIntoTexture is always true.
            passData.viewMatrix = camera.worldToCameraMatrix;
            passData.projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            builder.SetRenderFunc(
                (DrawObjectPassData data, RenderGraphContext ctx) =>
                {
                    if(!IsEnabled)
                    {
                        return;
                    }

                    ctx.cmd.SetViewProjectionMatrices(data.viewMatrix, data.projMatrix);

                    if (!(camera.cameraType == CameraType.Preview && camera.name == HNRenderPipelineUtils.PREVIEW_CAMERA_NAME))
                    {
                        if(hasCascadeShadow)
                        {
                            ctx.cmd.EnableShaderKeyword(GlobalKeywords.cascadeShadowMap);
                            if(isScreenSpaceShadow)
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

                        // Cluster culling light shader keyword + globals
                        if (data.hasLightMask)
                        {
                            ctx.cmd.EnableShaderKeyword(
                                GlobalKeywords.clusterCullingLight);
                            ctx.cmd.SetGlobalBuffer(
                                ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer,
                                data.lightMaskBuffer);
                        }

                        // Light data buffer (set only when the slot is connected —
                        // avoids binding an invalid handle when there is no light pass)
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
            // No disposable resources held by this pass.
        }

        // ── Helpers ──

        /// <summary>
        /// Creates the renderer list locally from <see cref="RendererListParams"/>.
        /// Returns a default (invalid) handle when culling results are unavailable.
        /// </summary>
        /// <param name="renderGraph">The render graph to create the list in.</param>
        /// <returns>The created renderer list handle, or a default handle.</returns>
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
        /// Render graph pass data container for <see cref="DrawObjectPass"/>.
        /// </summary>
        private sealed class DrawObjectPassData
        {
            /// <summary>
            /// The color target texture handle.
            /// </summary>
            public TextureHandle colorTarget;

            /// <summary>
            /// The depth target texture handle.
            /// </summary>
            public TextureHandle depthTarget;

            /// <summary>
            /// The light data compute buffer handle.
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// The reflection probe atlas texture handle.
            /// </summary>
            public TextureHandle reflectionProbeAtlas;

            /// <summary>
            /// The cluster culling reflection probe mask buffer handle.
            /// </summary>
            public ComputeBufferHandle probeMaskBuffer;

            /// <summary>
            /// The cluster culling reflection probe data buffer handle.
            /// </summary>
            public ComputeBufferHandle probeDatasBuffer;

            /// <summary>
            /// The cluster culling light mask buffer handle.
            /// </summary>
            public ComputeBufferHandle lightMaskBuffer;

            /// <summary>
            /// The renderer list handle.
            /// </summary>
            public RendererListHandle rendererList;

            // /// <summary>
            // /// Whether the render function should set lighting globals.
            // /// </summary>
            // public bool setLightGlobals;

            /// <summary>
            /// Whether the probe keyword + globals should be enabled.
            /// </summary>
            public bool enableProbeKeyword;

            /// <summary>
            /// Whether the cluster culling light mask buffer is bound.
            /// </summary>
            public bool hasLightMask;

            /// <summary>
            /// Whether the light data buffer is bound.
            /// </summary>
            public bool hasLightDatas;

            /// <summary>
            /// The view matrix for this pass's camera.
            /// </summary>
            public Matrix4x4 viewMatrix;

            /// <summary>
            /// The GPU projection matrix for this pass's camera.
            /// </summary>
            public Matrix4x4 projMatrix;
        }
    }
}
