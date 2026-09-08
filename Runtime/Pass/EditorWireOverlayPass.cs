using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// Draws the Editor wire overlay (gizmos, selection outlines, etc.) into the color target.
    /// New <see cref="Pass"/>-based replacement for the legacy
    /// <see cref="EditorWireOverlayPass"/> (<c>PassBase</c>).
    /// </summary>
    /// <remarks>
    /// <para>Inputs (connected from upstream, e.g. <c>DrawObjectPass</c>):</para>
    /// <list type="bullet">
    ///   <item><b>ColorTarget</b> — the color buffer into which the wire overlay is drawn.</item>
    /// </list>
    /// <para>
    /// Uses the shared texture model: the color target is allocated by the upstream
    /// chain head pass and this pass renders into the same buffer.
    /// </para>
    /// <para>
    /// This pass is only active in the Unity Editor and only for Scene View cameras.
    /// The entire <see cref="Record"/> implementation is wrapped in <c>#if UNITY_EDITOR</c>.
    /// The render function calls <c>ctx.renderContext.DrawWireOverlay(camera)</c>,
    /// matching the legacy <see cref="EditorWireOverlayPass"/> behavior.
    /// </para>
    /// <para>
    /// <b>Outputs (pass-through for downstream chaining):</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><b>ColorTargetOutput</b> — pass-through of the input color target
    ///   so downstream passes can connect without a separate resource node.</item>
    /// </list>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class EditorWireOverlayPass : Pass
    {
        /// <summary>
        /// The constant pass name string used for registration and identification.
        /// Matches the legacy <see cref="EditorWireOverlayPass.PassName"/> pattern.
        /// </summary>
        public const string PassNameConst = "Editor Wire Overlay";

        // ── Slots ──

        /// <summary>
        /// Gets the output color target slot.
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetSlot { get; private set; }

        /// <summary>
        /// Gets the output color target slot (pass-through of the input
        /// <see cref="ColorTargetSlot"/> handle for downstream chaining).
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetOutputSlot { get; private set; }

        // ── Camera context ──

        private CameraContext? cameraContext;

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorWireOverlayPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public EditorWireOverlayPass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorWireOverlayPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public EditorWireOverlayPass(string passName)
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

            ColorTargetOutputSlot = new TextureSlot("ColorTargetOutput", SlotDirection.Output);
            RegisterSlot(ColorTargetOutputSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Stores the camera context so the camera can be accessed during
        /// <see cref="Record"/> for the <c>DrawWireOverlay</c> call.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// Reads the upstream color target (shared texture model — allocated by
        /// <c>DrawObjectPass</c>) and draws the Editor wire overlay using
        /// <c>ctx.renderContext.DrawWireOverlay(camera)</c> — identical logic to
        /// the legacy <see cref="EditorWireOverlayPass.Record"/>.
        /// </para>
        /// <para>
        /// Only active for Scene View cameras. The entire implementation is
        /// wrapped in <c>#if UNITY_EDITOR</c> so it is stripped from player builds.
        /// </para>
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
#if UNITY_EDITOR
            if (ColorTargetSlot == null)
            {
                IsEnabled &= false;
            }

            if (cameraContext == null)
            {
                IsEnabled &= false;
            }

            Camera camera = cameraContext.Camera;
            if (camera.cameraType != CameraType.SceneView)
            {
                IsEnabled &= false;
            }

            if (!ColorTargetSlot.IsConnected)
            {
                IsEnabled &= false;
            }

            using var builder = renderGraph.AddRenderPass<EditorWireOverlayPassData>(
                PassName, out var passData);

            builder.AllowPassCulling(false);

            // ── Input slot: use upstream color target (shared texture model) ──

            TextureHandle colorTarget = ColorTargetSlot.ReadHandle();

            // Guard against an invalid upstream chain (e.g. a frame where culling
            // failed so the producer pass skipped recording).
            if (!colorTarget.IsValid())
            {
                IsEnabled &= false;
            }

            // Pass-through the input color handle to the output slot so
            // downstream passes can chain from this pass's output.
            if (ColorTargetOutputSlot != null)
            {
                ColorTargetOutputSlot.SetHandle(colorTarget);
            }

            passData.colorTarget = builder.UseColorBuffer(colorTarget, 0);

            // ── Render function: draw wire overlay (same logic as legacy EditorWireOverlayPass) ──

            passData.camera = camera;
            builder.SetRenderFunc(
                (EditorWireOverlayPassData data, RenderGraphContext ctx) =>
                {
                    if(!IsEnabled)
                    {
                        return;
                    }

                    ctx.renderContext.ExecuteCommandBuffer(ctx.cmd);
                    ctx.cmd.Clear();
                    ctx.renderContext.DrawWireOverlay(data.camera);
                });
#endif
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // No disposable resources held by this pass.
        }

        // ── Pass data ──

        /// <summary>
        /// Render graph pass data container for <see cref="EditorWireOverlayPass"/>.
        /// </summary>
        private sealed class EditorWireOverlayPassData
        {
            /// <summary>
            /// The color target texture handle.
            /// </summary>
            public TextureHandle colorTarget;

            /// <summary>
            /// The camera whose wire overlay is drawn.
            /// </summary>
            public Camera camera;
        }
    }
}
