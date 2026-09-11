using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// 把 Editor 线框叠加层（gizmos、选中轮廓等）绘制进颜色目标。
    /// 旧 <see cref="EditorWireOverlayPass"/>（<c>PassBase</c>）的 <see cref="Pass"/> 版替代。
    /// </summary>
    /// <remarks>
    /// <para>输入（从上游连接，如 <c>DrawObjectPass</c>）：</para>
    /// <list type="bullet">
    ///   <item><b>ColorTarget</b> —— 绘制线框叠加层所用的颜色缓冲。</item>
    /// </list>
    /// <para>
    /// 使用共享纹理模型：颜色目标由上游链头 pass 分配，本 pass 渲染进同一缓冲。
    /// </para>
    /// <para>
    /// 本 pass 仅在 Unity Editor 中且仅对 SceneView 相机生效。
    /// 整个 <see cref="Record"/> 实现被 <c>#if UNITY_EDITOR</c> 包裹。
    /// 渲染函数调用 <c>ctx.renderContext.DrawWireOverlay(camera)</c>，
    /// 与旧 <see cref="EditorWireOverlayPass"/> 行为一致。
    /// </para>
    /// <para>
    /// <b>输出（透传以支持下游链式连接）：</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><b>ColorTargetOutput</b> —— 输入颜色目标的透传，
    ///   使下游 pass 无需独立资源节点即可连接。</item>
    /// </list>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class EditorWireOverlayPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。
        /// 遵循旧 <see cref="EditorWireOverlayPass.PassName"/> 模式。
        /// </summary>
        public const string PassNameConst = "Editor Wire Overlay";

        // ── Slot ──

        /// <summary>
        /// 颜色目标输入 slot。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetSlot { get; private set; }

        /// <summary>
        /// 颜色目标输出 slot（输入 <see cref="ColorTargetSlot"/> 句柄的透传，
        /// 供下游链式连接）。<see cref="SetupSlots"/> 调用后可用。
        /// </summary>
        public TextureSlot ColorTargetOutputSlot { get; private set; }

        // ── 相机上下文 ──

        private CameraContext cameraContext;

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="EditorWireOverlayPass"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// </remarks>
        public EditorWireOverlayPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="EditorWireOverlayPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public EditorWireOverlayPass(string passName)
            : base(passName)
        {
        }

        // ── 生命周期 ──

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
        /// 保存相机上下文，使 <see cref="Record"/> 期间能访问相机以调用
        /// <c>DrawWireOverlay</c>。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// 读取上游颜色目标（共享纹理模型 —— 由 <c>DrawObjectPass</c> 分配），
        /// 并用 <c>ctx.renderContext.DrawWireOverlay(camera)</c> 绘制 Editor 线框叠加层
        /// —— 与旧 <see cref="EditorWireOverlayPass.Record"/> 逻辑相同。
        /// </para>
        /// <para>
        /// 仅对 SceneView 相机生效。整个实现被 <c>#if UNITY_EDITOR</c> 包裹，
        /// 因此在 Player 构建中被剔除。
        /// </para>
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
#if UNITY_EDITOR
            // ── 预检：必须在 AddRenderPass 之前完成 ──
            // 输入不可用时直接跳过本帧、不产生 pass，避免执行期缺少 RenderFunc。
            // 只跳过本帧，不改动 IsEnabled（外部动态开关）。
            if (ColorTargetSlot == null
                || cameraContext == null
                || !ColorTargetSlot.IsConnected
                || !ColorTargetSlot.HasHandle)
            {
                return;
            }

            Camera camera = cameraContext.Camera;
            if (camera == null || camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            // ── 输入 slot：使用上游颜色目标（共享纹理模型）──

            TextureHandle colorTarget = ColorTargetSlot.ReadHandle();

            // 防护无效的上游链（如某帧裁剪失败使生产 pass 跳过记录）。
            if (!colorTarget.IsValid())
            {
                return;
            }

            // 把输入颜色句柄透传到输出 slot，
            // 使下游 pass 能从本 pass 输出继续链式连接。
            if (ColorTargetOutputSlot != null)
            {
                ColorTargetOutputSlot.SetHandle(colorTarget);
            }

            using var builder = renderGraph.AddRenderPass<EditorWireOverlayPassData>(
                PassName, out var passData);

            builder.AllowPassCulling(false);

            passData.colorTarget = builder.UseColorBuffer(colorTarget, 0);

            // ── 渲染函数：绘制线框叠加层（与旧 EditorWireOverlayPass 相同逻辑）──

            passData.camera = camera;
            builder.SetRenderFunc(
                (EditorWireOverlayPassData data, RenderGraphContext ctx) =>
                {
                    if (!IsEnabled)
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
            // 本 pass 不持有可释放资源。
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="EditorWireOverlayPass"/> 的渲染图 pass 数据容器。
        /// </summary>
        private sealed class EditorWireOverlayPassData
        {
            /// <summary>
            /// 颜色目标纹理句柄。
            /// </summary>
            public TextureHandle colorTarget;

            /// <summary>
            /// 绘制线框叠加层所用相机。
            /// </summary>
            public Camera camera;
        }
    }
}
