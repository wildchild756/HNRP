// <copyright file="BuiltinSkyPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// 用 Unity 内置天空盒渲染 pass。
    /// </summary>
    [Pass(PassNameConst)]
    public sealed class BuiltinSkyPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。与旧 <see cref="BuiltinSkyPass.PassName"/> 一致。
        /// </summary>
        public const string PassNameConst = "Builtin Sky";

        // ── Slot ──

        /// <summary>
        /// 颜色目标输入 slot。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetSlot { get; private set; }

        /// <summary>
        /// 深度目标输入 slot。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot DepthTargetSlot { get; private set; }

        /// <summary>
        /// 颜色目标输出 slot（输入 <see cref="ColorTargetSlot"/> 句柄的透传，
        /// 供下游 pass 链式连接）。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetOutputSlot { get; private set; }

        /// <summary>
        /// 深度目标输出 slot（输入 <see cref="DepthTargetSlot"/> 句柄的透传，
        /// 供下游 pass 链式连接）。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot DepthTargetOutputSlot { get; private set; }

        // ── 相机上下文 ──

        private CameraContext cameraContext;

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="BuiltinSkyPass"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// </remarks>
        public BuiltinSkyPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="BuiltinSkyPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public BuiltinSkyPass(string passName)
            : base(passName)
        {
        }

        // ── 生命周期 ──

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
        /// 保存相机上下文，使 <see cref="Record"/> 期间能从 <c>Camera</c> 构建
        /// 天空盒渲染器列表。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 读取上游颜色与深度目标（共享纹理模型 —— 由 <c>DrawObjectPass</c> 分配），
        /// 并设置一个用 <c>ctx.renderContext.CreateSkyboxRendererList</c> 绘制天空盒的
        /// 渲染函数 —— 与旧 <see cref="BuiltinSkyPass.Record"/> 逻辑相同。
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            if (ColorTargetSlot == null || DepthTargetSlot == null
                || cameraContext == null
                || !ColorTargetSlot.IsConnected || !DepthTargetSlot.IsConnected)
            {
                IsEnabled = false;
                return;
            }

            if (cameraContext.Camera == null
                || cameraContext.Camera.clearFlags != CameraClearFlags.Skybox
                || RenderSettings.skybox == null)
            {
                IsEnabled = false;
                return;
            }

            using var builder = renderGraph.AddRenderPass<BuiltinSkyPassData>(
                PassName, out var passData);

            builder.AllowPassCulling(false);

            // ── 输入 slot：使用上游颜色 / 深度目标（共享纹理模型）──

            TextureHandle colorTarget = ColorTargetSlot.ReadHandle();
            TextureHandle depthTarget = DepthTargetSlot.ReadHandle();

            // 防护无效的上游链（如某帧裁剪失败使生产 pass 跳过记录）。
            // 跳过本 pass，而不是绑定无效句柄（后者会在渲染图执行期抛错）。
            if (!colorTarget.IsValid() || !depthTarget.IsValid())
            {
                IsEnabled = false;
                return;
            }

            // 把输入颜色 / 深度句柄透传到输出 slot，
            // 使下游 pass 能从本 pass 输出继续链式连接。
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

            // ── 渲染函数：绘制天空盒（与旧 BuiltinSkyPass 相同逻辑）──

            passData.camera = cameraContext.Camera;
            builder.SetRenderFunc(
                (BuiltinSkyPassData data, RenderGraphContext ctx) =>
                {
                    if (!IsEnabled)
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
            // 本 pass 不持有可释放资源。
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="BuiltinSkyPass"/> 的渲染图 pass 数据容器。
        /// </summary>
        private sealed class BuiltinSkyPassData
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
            /// 渲染天空盒的相机。
            /// </summary>
            public Camera camera;
        }
    }
}
