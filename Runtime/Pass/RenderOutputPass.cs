// <copyright file="RenderOutputPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 通过 blit 把最终渲染颜色输出到相机后备缓冲。
    /// 旧 <see cref="RenderOutput"/>（<c>PassBase</c>）的 <see cref="Pass"/> 版替代。
    /// </summary>
    /// <remarks>
    /// 本 pass 消费上游输出 slot（如 <c>ColorBufferInput</c>）的颜色纹理，
    /// 用 <see cref="Blitter"/> 工具 blit 到相机目标。
    /// </remarks>
    [Pass("Render Output")]
    public sealed class RenderOutputPass : Pass
    {
        /// <summary>
        /// 获取本 pass 声明的颜色目标输入 slot。
        /// <see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetSlot { get; private set; }

        /// <summary>
        /// 获取或设置输出是否垂直翻转。默认为 <c>false</c>。
        /// </summary>
        public bool Flip
        {
            get => flip;
            set => flip = value;
        }

        /// <summary>
        /// 输出是否垂直翻转。默认为 <c>false</c>。
        /// </summary>
        [SerializeField]
        private bool flip;

        private CameraContext cameraContext;

        /// <summary>
        /// 初始化 <see cref="RenderOutputPass"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// </remarks>
        public RenderOutputPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="RenderOutputPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的名称。默认为 "Render Output"。
        /// </param>
        public RenderOutputPass(string passName = "Render Output")
            : base(passName)
        {
        }

        /// <inheritdoc />
        public override void SetupSlots()
        {
            ColorTargetSlot = new TextureSlot("ColorTarget", SlotDirection.Input);
            RegisterSlot(ColorTargetSlot);
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
            if (ColorTargetSlot == null || !ColorTargetSlot.IsConnected || cameraContext == null)
            {
                return;
            }

            if (cameraContext.Camera == null)
            {
                return;
            }

            TextureHandle backBuffer;
            if (cameraContext.Camera.cameraType == CameraType.Reflection)
            {
                if (cameraContext.CustomTargetRTHandle != null)
                {
                    // 实时探针面渲染：HNRP 自己驱动面，用显式 RTHandle 指向具体面。
                    backBuffer = renderGraph.ImportTexture(cameraContext.CustomTargetRTHandle);
                }
                else
                {
                    // Bake/custom 路径由 ReflectionProbe.RenderProbe / Camera.RenderToCubemap
                    // 驱动：Unity 内部把相机 target 设为临时 cubemap RT 并逐面渲染，SRP 应输出到
                    // camera.targetTexture（CameraTarget），面保持 Unity 已绑定的当前面。
                    backBuffer = renderGraph.ImportBackbuffer(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
                }
            }
            else
            {
                backBuffer = renderGraph.ImportBackbuffer(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            }

            TextureHandle inputHandle = ColorTargetSlot.ReadHandle();
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
                    if (!IsEnabled)
                    {
                        return;
                    }

                    var propertyBlock =
                        ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                    var scaleBias = data.flip
                        ? new Vector4(1.0f, -1.0f, 0.0f, 1.0f)
                        : new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
                    if (data.TargetFace != CubemapFace.Unknown)
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
        /// <see cref="RenderOutputPass"/> 的渲染图 pass 数据。
        /// </summary>
        private sealed class RenderOutputData
        {
            /// <summary>
            /// 要 blit 到后备缓冲的输入颜色纹理。
            /// </summary>
            public TextureHandle inputTexture;

            /// <summary>
            /// 接收 blit 输出的相机后备缓冲。
            /// </summary>
            public TextureHandle backBuffer;

            /// <summary>目标 cubemap 面（非 cubemap 输出时该字段无效）。</summary>
            public CubemapFace TargetFace;

            /// <summary>目标深度切片。</summary>
            public int TargetDepthSlice;

            /// <summary>
            /// 输出是否垂直翻转。
            /// </summary>
            public bool flip;
        }
    }
}
