// <copyright file="PassSlot.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// 定义 <see cref="PassSlot"/> 的方向。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><see cref="Input"/> —— 从已连接的输出 slot 读取资源句柄。</item>
    ///   <item><see cref="Output"/> —— 创建输入 slot 可读取的资源句柄。</item>
    /// </list>
    /// </remarks>
    public enum SlotDirection
    {
        /// <summary>
        /// 输入 slot：从已连接的输出句柄读取。
        /// </summary>
        Input,

        /// <summary>
        /// 输出 slot：为下游输入创建资源句柄。
        /// </summary>
        Output,
    }

    /// <summary>
    /// 基于名称的渲染 pass slot 抽象基类。
    /// 用名称驱动模型取代旧的基于索引的 slot 系统。
    /// </summary>
    /// <remarks>
    /// <para><b>连接模型：</b></para>
    /// <list type="bullet">
    ///   <item>输出 slot 通过 <see cref="PassSlot{T}.SetHandle"/> 发布资源句柄。</item>
    ///   <item>输出 slot 通过 <see cref="Connect"/> 连接一个输入 slot。</item>
    ///   <item>输入 slot 通过 <see cref="PassSlot{T}.ReadHandle"/> 读取已连接输出的句柄。</item>
    /// </list>
    /// <para>
    /// 本类为纯 C# —— 无 <c>ScriptableObject</c> 继承、无 Unity 序列化特性。
    /// 设计为轻量，可在 Unity Editor 之外测试。
    /// </para>
    /// </remarks>
    public abstract class PassSlot
    {
        /// <summary>
        /// 获取本 slot 的名称。必须非空且在单个 pass 内唯一。
        /// </summary>
        public string SlotName { get; }

        /// <summary>
        /// 获取本 slot 的方向 —— <see cref="SlotDirection.Input"/> 或
        /// <see cref="SlotDirection.Output"/>。
        /// </summary>
        public SlotDirection Direction { get; }

        /// <summary>
        /// 获取本 slot 是否已连接。
        /// 对输入 slot，<see cref="Connect"/> 成功后为 <c>true</c>；
        /// 对输出 slot 恒为 <c>false</c>（一个输出可驱动多个输入，不跟踪自身连接状态）。
        /// </summary>
        public bool IsConnected { get; protected set; }

        /// <summary>
        /// 对输入 slot：本输入连接到的输出 slot。
        /// 对输出 slot：恒为 <c>null</c>。
        /// </summary>
        protected PassSlot connectedOutput;

        /// <summary>
        /// 拥有本 slot 的 <see cref="Pass"/>。由 <see cref="Pass.RegisterSlot"/> 设置；
        /// 构建期用于从连接推导 pass 级依赖。
        /// </summary>
        public Pass OwnerPass { get; internal set; }

        /// <summary>
        /// 对输入 slot：本输入连接到的输出 slot；未连接时为 <c>null</c>。
        /// 对输出 slot：恒为 <c>null</c>。
        /// </summary>
        public PassSlot ConnectedOutput => connectedOutput;

        /// <summary>
        /// 初始化 <see cref="PassSlot"/> 的新实例。
        /// </summary>
        /// <param name="slotName">
        /// slot 名称。必须非 null 且非空（纯空白被拒绝）。
        /// </param>
        /// <param name="direction">
        /// 本 slot 是 <see cref="SlotDirection.Input"/> 还是
        /// <see cref="SlotDirection.Output"/>。
        /// </param>
        /// <exception cref="ArgumentException">
        /// 当 <paramref name="slotName"/> 为 <c>null</c>、空或纯空白时抛出。
        /// </exception>
        protected PassSlot(string slotName, SlotDirection direction)
        {
            if (string.IsNullOrWhiteSpace(slotName))
            {
                throw new ArgumentException(
                    "Slot 名不能为 null、空或纯空白。",
                    nameof(slotName));
            }

            SlotName = slotName;
            Direction = direction;
        }

        /// <summary>
        /// 把本输出 slot 连接到给定输入 slot。
        /// 连接后，输入 slot 可调用 <see cref="PassSlot{T}.ReadHandle"/>
        /// 读取本输出的资源句柄。
        /// </summary>
        /// <param name="input">
        /// 要连接的输入 slot。必须为 <see cref="SlotDirection.Input"/>。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="input"/> 为 <c>null</c> 时抛出。
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// 当本 slot 不是输出，或 <paramref name="input"/> 不是输入时抛出。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// 当输出与输入 slot 携带不同资源类型时抛出。
        /// </exception>
        public virtual void Connect(PassSlot input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (Direction != SlotDirection.Output)
            {
                throw new InvalidOperationException(
                    "只有输出 slot 能发起连接。");
            }

            if (input.Direction != SlotDirection.Input)
            {
                throw new InvalidOperationException(
                    "只能把输出 slot 连接到输入 slot。");
            }

            if (!CanConnectTo(input))
            {
                throw new ArgumentException(
                    $"Slot 类型不匹配：{GetType().Name} 无法连接到 {input.GetType().Name}。" +
                    "输出与输入 slot 必须携带相同资源类型。");
            }

            input.connectedOutput = this;
            input.IsConnected = true;
        }

        /// <summary>
        /// 判断本输出 slot 能否连接到给定输入 slot。
        /// 基类允许任意连接；<see cref="PassSlot{T}"/> 覆写为要求资源类型匹配。
        /// </summary>
        /// <param name="input">要校验的输入 slot。</param>
        /// <returns>类型兼容时返回 <c>true</c>。</returns>
        protected virtual bool CanConnectTo(PassSlot input) => true;

        /// <summary>
        /// 清除本 slot 保存的句柄。在每帧 <c>Record</c> 前调用，防止读到上一帧的
        /// 过期句柄。
        /// </summary>
        public abstract void ResetHandle();
    }

    /// <summary>
    /// 强类型 <see cref="PassSlot"/>：资源句柄直接存于值类型字段，
    /// 消除逐帧装箱分配。
    /// </summary>
    /// <typeparam name="T">
    /// 本 slot 携带的渲染图资源句柄结构体（如 <see cref="TextureHandle"/>、
    /// <see cref="ComputeBufferHandle"/>、<see cref="RendererListHandle"/>）。
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// <b>零分配：</b>句柄直接存入类型化字段，<see cref="SetHandle"/> 永不对
    /// 结构体装箱，保持渲染循环无分配。
    /// </para>
    /// <para>
    /// <b><see cref="HasHandle"/> 语义：</b>对输出 slot，仅当 <see cref="SetHandle"/>
    /// 被调用<i>且</i>存储值通过 <see cref="IsValueValid"/> 时为 <c>true</c>。
    /// 对输入 slot，当已连接输出当前持有有效句柄时为 <c>true</c>。
    /// 默认（无效）句柄 —— 例如 <c>default(TextureHandle)</c> —— 视为"无句柄"。
    /// Pass 据此决定消费上游资源还是自建资源。
    /// </para>
    /// <para>
    /// <b><see cref="ResetHandle"/>：</b>每帧 <c>Record</c> 前调用，
    /// 防止读到上一帧的过期句柄。
    /// </para>
    /// </remarks>
    public class PassSlot<T> : PassSlot
    {
        /// <summary>
        /// 直接保存在本 slot 中的值类型资源句柄。
        /// </summary>
        private T value;

        /// <summary>
        /// 自上次 <see cref="ResetHandle"/> 后 <see cref="SetHandle"/> 是否被调用。
        /// </summary>
        private bool hasValue;

        /// <summary>
        /// 获取本 slot 当前是否持有有效资源句柄。对输出 slot，仅当
        /// <see cref="SetHandle"/> 以通过 <see cref="IsValueValid"/> 的值被调用后为
        /// <c>true</c>；对输入 slot，当已连接输出当前持有有效句柄时为 <c>true</c>。
        /// </summary>
        public bool HasHandle
        {
            get
            {
                if (Direction == SlotDirection.Output)
                {
                    return hasValue && IsValueValid(value);
                }

                // 输入：反映已连接输出保存的句柄。
                if (connectedOutput is PassSlot<T> output)
                {
                    return output.hasValue && IsValueValid(output.value);
                }

                return false;
            }
        }

        /// <summary>
        /// 校验保存的句柄值。基类接受任意值；具体 slot 委托给 Unity 句柄类型的
        /// 有效性检查（如 <see cref="TextureHandle.IsValid"/>）。
        /// </summary>
        /// <param name="value">要校验的句柄值。</param>
        /// <returns>句柄有效时返回 <c>true</c>。</returns>
        protected virtual bool IsValueValid(T value) => true;

        /// <summary>
        /// 初始化 <see cref="PassSlot{T}"/> 的新实例。
        /// </summary>
        /// <param name="slotName">slot 名称。</param>
        /// <param name="direction">本 slot 是输入还是输出。</param>
        public PassSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <summary>
        /// 为本 slot 设置资源句柄。输出 slot 用它发布真实渲染图句柄，
        /// 已连接输入 slot 经 <see cref="ReadHandle"/> 读取。值直接保存（零分配）。
        /// </summary>
        /// <param name="value">真实渲染图资源句柄。</param>
        public void SetHandle(T value)
        {
            this.value = value;
            hasValue = true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 把存储值重置为 <c>default</c> 并清除 has-value 标志。
        /// </remarks>
        public override void ResetHandle()
        {
            value = default;
            hasValue = false;
        }

        /// <summary>
        /// 读取与本 slot 关联的资源句柄。
        /// </summary>
        /// <returns>
        /// 对输出 slot：返回其自身保存的句柄。
        /// 对已连接的输入 slot：返回已连接输出的句柄。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 当本 slot 是尚未连接的输入时抛出。
        /// </exception>
        public T ReadHandle()
        {
            if (Direction == SlotDirection.Output)
            {
                return value;
            }

            // 方向为 Input
            if (!IsConnected)
            {
                throw new InvalidOperationException(
                    "输入 slot 未连接到输出 slot。请先在输出 slot 上调用 Connect()。");
            }

            return ((PassSlot<T>)connectedOutput).value;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 要求输入 slot 携带相同资源类型
        /// （即 <see cref="PassSlot{T}"/> 且 <typeparamref name="T"/> 相同）。
        /// </remarks>
        protected override bool CanConnectTo(PassSlot input) => input is PassSlot<T>;
    }

    #region 具体 Slot 类型

    /// <summary>
    /// 表示纹理资源的 <see cref="PassSlot{T}"/>。
    /// </summary>
    public class TextureSlot : PassSlot<TextureHandle>
    {
        /// <summary>
        /// 初始化 <see cref="TextureSlot"/> 的新实例。
        /// </summary>
        /// <param name="slotName">纹理 slot 的名称。</param>
        /// <param name="direction">本 slot 是输入还是输出。</param>
        public TextureSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <see cref="TextureHandle"/> 仅当引用真实渲染图纹理时有效 —— 默认句柄无效。
        /// </remarks>
        protected override bool IsValueValid(TextureHandle value) => value.IsValid();
    }

    /// <summary>
    /// 表示 compute buffer 资源的 <see cref="PassSlot{T}"/>。
    /// </summary>
    public class ComputeBufferSlot : PassSlot<ComputeBufferHandle>
    {
        /// <summary>
        /// 初始化 <see cref="ComputeBufferSlot"/> 的新实例。
        /// </summary>
        /// <param name="slotName">compute buffer slot 的名称。</param>
        /// <param name="direction">本 slot 是输入还是输出。</param>
        public ComputeBufferSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <see cref="ComputeBufferHandle"/> 仅当引用真实渲染图缓冲时有效 ——
        /// 默认句柄无效。
        /// </remarks>
        protected override bool IsValueValid(ComputeBufferHandle value) => value.IsValid();
    }

    #endregion
}
