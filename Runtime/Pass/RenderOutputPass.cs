// <copyright file="RenderOutputPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// Outputs the final rendered color to the camera backbuffer via a blit.
    /// New <see cref="Pass"/>-based replacement for the legacy
    /// <see cref="RenderOutput"/> (<c>PassBase</c>).
    /// </summary>
    /// <remarks>
    /// This pass consumes a color texture from an upstream output slot
    /// (e.g. <c>ColorBufferInput</c>) and blits it to the camera target
    /// using the <see cref="Blitter"/> utility.
    /// </remarks>
    [Pass("Render Output")]
    public sealed class RenderOutputPass : Pass
    {
        private TextureSlot? colorTargetSlot;
        private CameraContext? cameraContext;

        /// <summary>
        /// Gets the color target input slot declared by this pass.
        /// Available after <see cref="SetupSlots"/> is called.
        /// </summary>
        public TextureSlot? ColorTargetSlot => colorTargetSlot;

        /// <summary>
        /// Whether the output should be vertically flipped. Default <c>false</c>.
        /// </summary>
        [SerializeField]
        private bool m_Flip;

        /// <summary>
        /// Gets or sets a value indicating whether the output should be
        /// vertically flipped. Default is <c>false</c>.
        /// </summary>
        public bool Flip
        {
            get => m_Flip;
            set => m_Flip = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderOutputPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// Uses the default pass name "Render Output".
        /// </summary>
        public RenderOutputPass()
            : base("Render Output")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderOutputPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The name of this pass. Default is "Render Output".
        /// </param>
        public RenderOutputPass(string passName = "Render Output")
            : base(passName)
        {
        }

        /// <inheritdoc />
        public override void CopyFrom(Pass source)
        {
            if (source is RenderOutputPass s)
            {
                Flip = s.Flip;
            }
        }

        /// <inheritdoc />
        public override void SetupSlots()
        {
            colorTargetSlot = new TextureSlot("ColorTarget", SlotDirection.Input);
            RegisterSlot(colorTargetSlot);
        }

        /// <inheritdoc />
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
            Flip = context.Flip;
        }

        /// <inheritdoc />
        public override void Record(RenderGraph renderGraph)
        {
            if (colorTargetSlot == null || !colorTargetSlot.IsConnected)
            {
                IsEnabled &= false;
            }

            if (cameraContext == null)
            {
                IsEnabled &= false;
            }

            TextureHandle backBuffer;
            if (cameraContext.Camera.cameraType == CameraType.Reflection)
            {
                if(cameraContext.CustomTargetRTHandle != null)
                {
                    // Realtime probe 面渲染：HNRP 自己驱动面，用显式 RTHandle 指向具体面。
                    backBuffer = renderGraph.ImportTexture(cameraContext.CustomTargetRTHandle);
                }
                else
                {
                    // Bake/custom 路径由 ReflectionProbe.RenderProbe / Camera.RenderToCubemap
                    // 驱动：Unity 内部把相机 target 设为临时 cubemap RT 并逐面渲染，SRP 应输出到
                    // camera.targetTexture（CameraTarget），face 保持 Unity 已绑定的当前面。
                    backBuffer = renderGraph.ImportBackbuffer(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
                }
            }
            else
            {
                backBuffer = renderGraph.ImportBackbuffer(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            }

            TextureHandle inputHandle = colorTargetSlot.ReadHandle();
            if (!inputHandle.IsValid())
            {
                return;
            }

            using var builder = renderGraph.AddRenderPass<RenderOutputData>(
                PassName, out var passData);
            builder.AllowPassCulling(false);

            passData.inputTexture = builder.ReadTexture(inputHandle);
            passData.backBuffer = builder.UseColorBuffer(backBuffer, 0);
            passData.TargetFace = cameraContext.TargetFace;
            passData.TargetDepthSlice = cameraContext.TargetDepthSlice;
            passData.flip = Flip;

            builder.SetRenderFunc(
                (RenderOutputData data, RenderGraphContext ctx) =>
                {
                    if(!IsEnabled)
                    {
                        return;
                    }

                    var propertyBlock =
                        ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                    var scaleBias = data.flip
                        ? new Vector4(1.0f, -1.0f, 0.0f, 1.0f)
                        : new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
                    if(data.TargetFace != CubemapFace.Unknown)
                    {
                        ctx.cmd.SetRenderTarget(data.backBuffer, 0, data.TargetFace, data.TargetDepthSlice);
                        Blitter.BlitTexture(ctx.cmd, propertyBlock, data.inputTexture, scaleBias, 0, true);
                    }
                    else
                    {
                        Blitter.BlitCameraTexture(ctx.cmd, propertyBlock, data.inputTexture, data.backBuffer, scaleBias, 0, true);
                    }
                });
        }

        /// <summary>
        /// Render graph pass data for <see cref="RenderOutputPass"/>.
        /// </summary>
        private sealed class RenderOutputData
        {
            /// <summary>
            /// The input color texture to blit to the backbuffer.
            /// </summary>
            public TextureHandle inputTexture;

            /// <summary>
            /// The camera backbuffer that receives the blit output.
            /// </summary>
            public TextureHandle backBuffer;

            public CubemapFace TargetFace;

            public int TargetDepthSlice;

            /// <summary>
            /// Whether to vertically flip the output.
            /// </summary>
            public bool flip;
        }
    }
}
