using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// HNRP 自定义渲染管线中渲染 pass 的抽象基类。
    /// Pass 是可序列化的 C# 对象（非 ScriptableObject），定义渲染图内的一单位渲染工作。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass 由执行 <see cref="RenderGraphTemplate"/> 构建代码创建
    /// （Editor 与运行时共用同一份代码，方案 X）：每个模板直接用名称构造与
    /// 逐 pass 参数创建实例。<see cref="RenderGraphAsset"/> 不再序列化 pass 实例，
    /// 因此具体 pass 无需无参构造与 <c>CopyFrom</c> 拷贝例程。
    /// <c>[SerializeField]</c> 字段保留，作为模板构建代码与编辑器检查用的参数面。
    /// </para>
    /// <para>生命周期顺序：</para>
    /// <list type="number">
    ///   <item><see cref="SetupSlots"/> —— 声明输入/输出 slot（如 TextureSlot、ComputeBufferSlot）</item>
    ///   <item><see cref="PreRecord"/> —— 用相机上下文加载资源</item>
    ///   <item><see cref="Record"/> —— 向渲染图记录渲染命令</item>
    ///   <item><see cref="Cleanup"/> —— 释放本 pass 持有的资源</item>
    /// </list>
    /// <para>
    /// 子类应加 <c>[Pass("Name")]</c> 特性，以支持反射（Editor）或代码生成（Player）
    /// 的自动发现。
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class Pass
    {
        /// <summary>
        /// 本 pass 的实例名。必须非 null 且在单个渲染图内唯一。
        /// </summary>
        [SerializeField]
        private string passName;

        /// <summary>
        /// 本 pass 是否启用。为 <c>false</c> 时调用方（如 <c>CameraRenderer</c>）
        /// 跳过本 pass 的 <see cref="Record"/>。默认为 <c>true</c>。
        /// </summary>
        [SerializeField]
        private bool isEnabled = true;

        /// <summary>
        /// <see cref="SetupSlots"/> 中声明的本 pass 全部 slot 的注册表，
        /// 以 <see cref="PassSlot.SlotName"/> 为键。
        /// </summary>
        private readonly Dictionary<string, PassSlot> slots = new();

        /// <summary>
        /// 初始化 <see cref="Pass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的名称。必须非 null 且在单个渲染图内唯一。
        /// </param>
        protected Pass(string passName)
        {
            this.passName = passName;
        }

        /// <summary>
        /// 获取本 pass 的名称，在构造时设置。
        /// </summary>
        public string PassName => passName;

        /// <summary>
        /// 获取或设置本 pass 是否启用。为 <c>false</c> 时调用方
        /// （如 <c>CameraRenderer</c>）跳过本 pass 的 <see cref="Record"/>。
        /// 默认为 <c>true</c>。
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        /// <summary>
        /// 声明本 pass 的输入与输出 slot。在 <see cref="PreRecord"/> 之前的
        /// 设置阶段调用一次。
        /// </summary>
        /// <remarks>
        /// Slot 定义数据依赖 —— 渲染图据此自动推导执行顺序。典型 slot 类型：
        /// <c>TextureSlot</c>、<c>ComputeBufferSlot</c>、<c>RendererListSlot</c>。
        /// </remarks>
        public abstract void SetupSlots();

        /// <summary>
        /// 注册 <see cref="SetupSlots"/> 中声明的 slot。
        /// 重名注册覆盖已有条目。
        /// </summary>
        /// <param name="slot">要注册的 slot。不能为 <c>null</c>。</param>
        protected void RegisterSlot(PassSlot slot)
        {
            slots[slot.SlotName] = slot;
            slot.OwnerPass = this;
        }

        /// <summary>
        /// 按名称取 slot；未注册该名称时返回 <c>null</c>。
        /// </summary>
        /// <param name="name">要查找的 slot 名称。</param>
        /// <returns>注册的 <see cref="PassSlot"/>，找不到时为 <c>null</c>。</returns>
        public PassSlot GetSlot(string name) => slots.TryGetValue(name, out var slot) ? slot : null;

        /// <summary>
        /// 把本 pass 的输出 slot（<paramref name="sourceSlotName"/>）连接到目标
        /// pass 的输入 slot（<paramref name="targetSlotName"/>）。
        /// </summary>
        /// <param name="sourceSlotName">本 pass 的输出 slot 名称。</param>
        /// <param name="target">接收连接的输入 slot 所属的目标 pass。</param>
        /// <param name="targetSlotName">目标 pass 的输入 slot 名称。</param>
        /// <returns>
        /// 连接建立时返回 <c>true</c>；任一处 slot 缺失、方向不匹配
        /// （需输出→输入）或 slot 携带不同资源类型时返回 <c>false</c>。
        /// </returns>
        public bool TryConnect(string sourceSlotName, Pass target, string targetSlotName)
        {
            if (!slots.TryGetValue(sourceSlotName, out var sourceSlot)) return false;
            if (!target.slots.TryGetValue(targetSlotName, out var targetSlot)) return false;
            if (sourceSlot.Direction != SlotDirection.Output) return false;
            if (targetSlot.Direction != SlotDirection.Input) return false;
            try
            {
                sourceSlot.Connect(targetSlot);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 重置本 pass 的全部输出 slot 句柄。在每帧 Record 前调用，防止读到
        /// 上一帧的过期句柄。
        /// </summary>
        public void ResetSlotHandles()
        {
            foreach (PassSlot slot in slots.Values)
            {
                if (slot.Direction == SlotDirection.Output)
                {
                    slot.ResetHandle();
                }
            }
        }

        /// <summary>
        /// 用相机专属渲染上下文初始化本 pass。
        /// 在此方法中加载 shader、材质等资源。
        /// </summary>
        /// <param name="context">
        /// 提供相机专属数据的相机渲染上下文。
        /// </param>
        public abstract void PreRecord(RenderGraphAsset template, CameraContext context);

        /// <summary>
        /// 向渲染图记录渲染命令。仅在 <see cref="IsEnabled"/> 为 <c>true</c> 时调用。
        /// </summary>
        /// <param name="renderGraph">
        /// 要记录命令的渲染图。输出 slot 经 <c>renderGraph.CreateTexture</c> 等创建资源；
        /// 输入 slot 从已连接输出读取。
        /// </param>
        public abstract void Record(RenderGraph renderGraph);

        /// <summary>
        /// 释放本 pass 持有的资源。
        /// 默认实现为空操作。重写以释放材质、compute buffer 或其他可释放资源。
        /// </summary>
        public virtual void Cleanup()
        {
        }
    }
}
