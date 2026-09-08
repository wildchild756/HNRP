// <copyright file="BuiltinSkyPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;


namespace HN.HNRP
{
    /// <summary>
    /// Renders the skybox into the color and depth targets.
    /// New <see cref="Pass"/>-based replacement for the legacy
    /// <see cref="BuiltinSkyPass"/> (<c>PassBase</c>).
    /// </summary>
    /// <remarks>
    /// <para>Inputs (connected from upstream, e.g. <c>DrawObjectPass</c>):</para>
    /// <list type="bullet">
    ///   <item><b>ColorTarget</b> — the color buffer into which the skybox is rendered.</item>
    ///   <item><b>DepthTarget</b> — the depth buffer used for skybox depth writes.</item>
    /// </list>
    /// <para>
    /// Uses the shared texture model: the color/depth targets are allocated by the
    /// upstream chain head pass and this pass renders into the same buffers.
    /// The render function calls <c>ctx.renderContext.CreateSkyboxRendererList</c>
    /// followed by <c>ctx.cmd.DrawRendererList</c>, which renders the Unity skybox
    /// material assigned to the active camera. This is the same logic as the
    /// legacy <see cref="BuiltinSkyPass"/>.
    /// </para>
    /// <para>
    /// <b>Outputs (pass-through for downstream chaining):</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><b>ColorTargetOutput</b> — pass-through of the input color target
    ///   so downstream passes can connect without a separate resource node.</item>
    ///   <item><b>DepthTargetOutput</b> — pass-through of the input depth target
    ///   so downstream passes can connect without a separate resource node.</item>
    /// </list>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class BuiltinSkyPass : Pass
    {
        /// <summary>
        /// The constant pass name string used for registration and identification.
        /// Matches the legacy <see cref="BuiltinSkyPass.PassName"/>.
        /// </summary>
        public const string PassNameConst = "Builtin Sky";

        // ── Slots ──

        /// <summary>
        /// Gets the output color target slot.
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetSlot { get; private set; }

        /// <summary>
        /// Gets the output depth target slot.
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? DepthTargetSlot { get; private set; }

        /// <summary>
        /// Gets the output color target slot (pass-through of the input
        /// <see cref="ColorTargetSlot"/> handle for downstream chaining).
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetOutputSlot { get; private set; }

        /// <summary>
        /// Gets the output depth target slot (pass-through of the input
        /// <see cref="DepthTargetSlot"/> handle for downstream chaining).
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? DepthTargetOutputSlot { get; private set; }

        // ── Camera context ──

        private CameraContext? cameraContext;

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinSkyPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public BuiltinSkyPass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinSkyPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public BuiltinSkyPass(string passName)
            : base(passName)
        {
        }

        /// <inheritdoc />
        public override void CopyFrom(Pass source)
        {
            // No serialized parameters on this pass.
        }

        // ── Lifecycle ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            ColorTargetSlot = new TextureSlot("ColorTarget", SlotDirection.Input);
            RegisterSlot(ColorTargetSlot);
            DepthTargetSlot = new TextureSlot("DepthTarget", SlotDirection.Input);
            RegisterSlot(DepthTargetSlot);

            ColorTargetOutputSlot = new TextureSlot("ColorTargetOutput", SlotDirection.Output);
            RegisterSlot(ColorTargetOutputSlot);
            DepthTargetOutputSlot = new TextureSlot("DepthTargetOutput", SlotDirection.Output);
            RegisterSlot(DepthTargetOutputSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Stores the camera context so the skybox renderer list can be built
        /// from <c>Camera</c> during <see cref="Record"/>.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reads the upstream color and depth targets (shared texture model — allocated
        /// by <c>DrawObjectPass</c>) and sets a render function that draws the
        /// skybox using <c>ctx.renderContext.CreateSkyboxRendererList</c> — identical
        /// logic to the legacy <see cref="BuiltinSkyPass.Record"/>.
        /// </remarks>
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

            if (!ColorTargetSlot.IsConnected || !DepthTargetSlot.IsConnected)
            {
                IsEnabled &= false;
            }

            if(cameraContext.Camera.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null)
            {
                IsEnabled &= false;
            }

            using var builder = renderGraph.AddRenderPass<BuiltinSkyPassData>(
                PassName, out var passData);

            builder.AllowPassCulling(false);

            // ── Input slots: use upstream color / depth targets (shared texture model) ──

            TextureHandle colorTarget = ColorTargetSlot.ReadHandle();
            TextureHandle depthTarget = DepthTargetSlot.ReadHandle();

            // Guard against an invalid upstream chain (e.g. a frame where culling
            // failed so the producer pass skipped recording). Skip this pass
            // instead of binding an invalid handle, which would throw during
            // render graph execution.
            if (!colorTarget.IsValid() || !depthTarget.IsValid())
            {
                IsEnabled &= false;
            }

            // Pass-through the input color / depth handles to the output slots so
            // downstream passes can chain from this pass's outputs.
            if (ColorTargetOutputSlot != null)
            {
                ColorTargetOutputSlot.SetHandle(colorTarget);
            }

            if (DepthTargetOutputSlot != null)
            {
                DepthTargetOutputSlot.SetHandle(depthTarget);
            }

            passData.colorTarget = builder.UseColorBuffer(colorTarget, 0);
            passData.depthTarget = builder.UseDepthBuffer(depthTarget, DepthAccess.ReadWrite);

            // ── Render function: draw skybox (same logic as legacy BuiltinSkyPass) ──

            passData.camera = cameraContext.Camera;
            builder.SetRenderFunc(
                (BuiltinSkyPassData data, RenderGraphContext ctx) =>
                {
                    if(!IsEnabled)
                    {
                        return;
                    }

                    UnityEngine.Rendering.RendererList rendererList = ctx.renderContext.CreateSkyboxRendererList(data.camera);
                    ctx.cmd.DrawRendererList(rendererList);
                });
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // No disposable resources held by this pass.
        }

        // ── Pass data ──

        /// <summary>
        /// Render graph pass data container for <see cref="BuiltinSkyPass"/>.
        /// </summary>
        private sealed class BuiltinSkyPassData
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
            /// The camera whose skybox is rendered.
            /// </summary>
            public Camera camera;
        }
    }
}
