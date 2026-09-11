// <copyright file="CameraRenderer.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 每相机独立的渲染器。
    /// 每个 <see cref="CameraRenderer"/> 拥有运行时的 <see cref="List{Pass}"/>、
    /// 一个 <see cref="CameraContext"/> 与当前 <see cref="RenderGraphAsset"/> 模板引用。
    /// 它负责从模板构建 pass、管理 pass 生命周期，并为单个相机执行渲染图。
    /// </summary>
    /// <remarks>
    /// <para><b>架构说明</b>（ADR-002、ADR-011）：
    /// <see cref="RenderGraphAsset"/> 是静态蓝图（ScriptableObject）；
    /// <see cref="CameraRenderer"/> 每相机拥有运行时 pass 列表。
    /// </para>
    /// <para>
    /// 渲染循环调用 <see cref="Build"/>（或 <see cref="Reset"/>）填充 pass 列表，
    /// 之后每帧调用 <see cref="Render"/> 按顺序执行 pass。
    /// </para>
    /// </remarks>
    /// <seealso cref="Pass"/>
    /// <seealso cref="CameraContext"/>
    /// <seealso cref="RenderGraphAsset"/>
    public class CameraRenderer
    {
        /// <summary>
        /// 本渲染器拥有的运行时 <see cref="Pass"/> 实例的有序列表。
        /// 由 <see cref="Build"/> 或 <see cref="Reset"/> 填充。
        /// </summary>
        public List<Pass> Passes { get; private set; } = new();

        /// <summary>
        /// 当前 <see cref="RenderGraphAsset"/> 模板。首次调用
        /// <see cref="Build"/> 或 <see cref="Reset"/> 之前为 <c>null</c>。
        /// </summary>
        public RenderGraphAsset CurrentTemplate { get; private set; }

        /// <summary>
        /// 当前 pass 列表对应的模板参数修订号。与
        /// <see cref="RenderGraphAsset.ParameterRevision"/> 不一致时需重建 pass。
        /// </summary>
        private int CurrentRevision { get; set; }

        /// <summary>
        /// 通过 <see cref="Connect"/> 添加的 slot 连接的内部存储。
        /// 在 <see cref="Build"/> 或 <see cref="Reset"/> 期间完成接线。
        /// </summary>
        private readonly List<SlotConnection> manualConnections = new();

        /// <summary>
        /// 初始化 <see cref="CameraRenderer"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 每帧渲染上下文不再由渲染器持有：<see cref="Render"/> 以参数接收
        /// <see cref="CameraContext"/>，避免跨帧悬挂引用已释放的帧上下文。
        /// </remarks>
        public CameraRenderer()
        {
        }

        /// <summary>
        /// 从 <see cref="RenderGraphAsset"/> 模板构建运行时 pass 列表。
        /// 调用 <see cref="RenderGraphAsset.Build"/> —— 该实现会执行模板构建代码
        /// 创建 pass（方案 X）、接线连接，并只保留启用 pass。
        /// </summary>
        /// <param name="template">
        /// 渲染图模板资产。不能为 <c>null</c>。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="template"/> 为 <c>null</c> 时抛出。
        /// </exception>
        public void Build(RenderGraphAsset template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            // 已为同一模板、同一参数修订构建过：复用已有 pass 实例。
            // pass 跨帧存活，其持有的资源（如阴影 atlas、驻留分配表）才能跨帧保留。
            // 参数缓存 / 设置变化会自增 ParameterRevision，据此强制重建，
            // 避免仅比较模板引用相等导致参数改动不生效。
            if (CurrentTemplate == template
                && Passes.Count > 0
                && CurrentRevision == template.ParameterRevision)
            {
                return;
            }

            DisposePasses();

            CurrentTemplate = template;
            CurrentRevision = template.ParameterRevision;
            Passes = template.Build() ?? new List<Pass>();

            WireManualConnections();
        }

        /// <summary>
        /// 创建指定类型 <typeparamref name="T"/> 的新 pass（传入实例名）并追加到
        /// pass 列表。
        /// </summary>
        /// <typeparam name="T">
        /// 要实例化的 <see cref="Pass"/> 具体子类。需提供接受单个
        /// <see cref="string"/> 参数（pass 名）的公开构造函数。
        /// </typeparam>
        /// <param name="name">新 pass 的实例名。</param>
        /// <returns>新创建的 pass 实例。</returns>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="name"/> 为 <c>null</c> 时抛出。
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// 当 <typeparamref name="T"/> 无法用字符串构造实例化时抛出。
        /// </exception>
        public T AddPass<T>(string name)
            where T : Pass
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            // 通过字符串构造实例化，与模板构建代码的构造路径一致。
            Pass instance;
            try
            {
                instance = (Pass)Activator.CreateInstance(typeof(T), name);
            }
            catch (MissingMethodException ex)
            {
                throw new InvalidOperationException(
                    $"Pass 类型 '{typeof(T).FullName}' 没有接受单个 string 参数的公开构造函数。 " +
                    $"所有 Pass 子类必须实现：public {typeof(T).Name}(string name) : base(name) {{ }}",
                    ex);
            }

            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"无法实例化 Pass 类型 '{typeof(T).FullName}'。");
            }

            Passes.Add(instance);
            return (T)instance;
        }

        /// <summary>
        /// 按实例名从运行时列表移除 pass。名字不存在时为空操作。
        /// </summary>
        /// <param name="name">要移除的 pass 实例名。</param>
        public void RemovePass(string name)
        {
            Passes.RemoveAll(p => p.PassName == name);
        }

        /// <summary>
        /// 按实例名查找指定类型 <typeparamref name="T"/> 的 pass。
        /// </summary>
        /// <typeparam name="T">期望的 pass 类型。</typeparam>
        /// <param name="name">要查找的 pass 实例名。</param>
        /// <returns>
        /// 类型为 <typeparamref name="T"/> 的匹配 pass，找不到时为 <c>null</c>。
        /// </returns>
        public T FindPass<T>(string name)
            where T : Pass
        {
            foreach (Pass pass in Passes)
            {
                if (pass.PassName == name && pass is T typedPass)
                {
                    return typedPass;
                }
            }

            return null;
        }

        /// <summary>
        /// 切换 pass 的启用状态。禁用后 <see cref="Render"/> 会跳过该 pass。
        /// </summary>
        /// <param name="name">pass 实例名。</param>
        /// <param name="enabled">目标启用状态。</param>
        public void SetPassEnabled(string name, bool enabled)
        {
            foreach (Pass pass in Passes)
            {
                if (pass.PassName == name)
                {
                    pass.IsEnabled = enabled;
                    return;
                }
            }
        }

        /// <summary>
        /// 把一个 pass 的输出 slot 与另一个 pass 的输入 slot 按名称相连。
        /// 连接在下次 <see cref="Build"/> 或 <see cref="Reset"/> 时接线。
        /// </summary>
        /// <param name="sourcePass">源 pass 的实例名。</param>
        /// <param name="sourceSlot">源 pass 上的输出 slot 名。</param>
        /// <param name="targetPass">目标 pass 的实例名。</param>
        /// <param name="targetSlot">目标 pass 上的输入 slot 名。</param>
        /// <remarks>
        /// <para>
        /// 连接在构建期解析。若 pass 暴露具名 slot（未来 API），实际数据流接线
        /// 会自动完成。当前仅存储连接记录，以备向前兼容。
        /// </para>
        /// </remarks>
        public void Connect(
            string sourcePass,
            string sourceSlot,
            string targetPass,
            string targetSlot)
        {
            manualConnections.Add(
                SlotConnection.Create(sourcePass, sourceSlot, targetPass, targetSlot));
        }

        /// <summary>
        /// 从新模板重建 pass 列表并重置全部运行时状态。
        /// 清空手动添加的 pass 并重新调用 <see cref="RenderGraphAsset.Build"/>。
        /// </summary>
        /// <param name="newTemplate">
        /// 新渲染图模板资产。不能为 <c>null</c>。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="newTemplate"/> 为 <c>null</c> 时抛出。
        /// </exception>
        public void Reset(RenderGraphAsset newTemplate)
        {
            if (newTemplate == null)
            {
                throw new ArgumentNullException(nameof(newTemplate));
            }

            manualConnections.Clear();
            DisposePasses();
            Build(newTemplate);
        }

        /// <summary>
        /// 为当前帧执行全部启用 pass。
        /// </summary>
        /// <param name="renderGraph">要记录命令的渲染图。</param>
        /// <param name="context">当前帧的每相机渲染上下文（纯帧级，不在本类留存）。</param>
        /// <remarks>
        /// <para><b>执行顺序：</b></para>
        /// <list type="number">
        ///   <item>先对<b>全部</b> pass（含被禁用者）调用
        ///     <see cref="Pass.ResetSlotHandles"/> —— 清空上一帧的输出 slot 句柄，
        ///     避免禁用 pass 的旧句柄被下游误读</item>
        ///   <item>再对每个<b>启用</b>的 pass 依次调用
        ///     <see cref="Pass.PreRecord"/>（用相机上下文加载资源）与
        ///     <see cref="Pass.Record"/>（记录渲染图命令）</item>
        /// </list>
        /// <para>
        /// 刻意不在每帧调用 <see cref="Pass.SetupSlots"/>：slot 在
        /// <see cref="Build(RenderGraphAsset)"/> 期间声明一次（经由
        /// <c>RenderGraphAsset.Build</c>），因此构建期建立的连接与每帧使用的
        /// 实例一致。
        /// </para>
        /// <para>
        /// 刻意不在每帧调用 <see cref="Pass.Cleanup"/>：pass 实例跨帧复用，
        /// 其资源须跨帧保留；仅在重建模板或 <see cref="Dispose"/> 时释放。
        /// </para>
        /// </remarks>
        public void Render(RenderGraph renderGraph, CameraContext context)
        {
            // ── 重置全部 pass（含被禁用者）的输出 slot 句柄 ──
            // 禁用 pass 不执行 Record，若不重置会残留上一帧句柄，被下游
            // 消费方/全局绑定误判为「本帧已产出」。
            for (int i = 0; i < Passes.Count; i++)
            {
                Passes[i].ResetSlotHandles();
            }

            // ── 执行每个启用 pass ──
            foreach (Pass pass in Passes)
            {
                if (!pass.IsEnabled)
                {
                    continue;
                }

                pass.PreRecord(CurrentTemplate, context);
                pass.Record(renderGraph);
            }
        }

        /// <summary>
        /// 释放当前 pass 列表：对每个 pass 调用 <see cref="Pass.Cleanup"/>，
        /// 清空列表并解除模板引用。在重建模板或销毁渲染器时调用。
        /// </summary>
        public void Dispose()
        {
            DisposePasses();
            manualConnections.Clear();
        }

        /// <summary>
        /// 释放 pass 列表持有的资源并清空列表（不清理手动连接）。
        /// </summary>
        private void DisposePasses()
        {
            foreach (Pass pass in Passes)
            {
                pass?.Cleanup();
            }

            Passes.Clear();
            CurrentTemplate = null;
            CurrentRevision = 0;
        }

        /// <summary>
        /// 接线手动添加的 <see cref="SlotConnection"/> 条目。
        /// 把 pass 名解析到实例并连接其 slot。
        /// </summary>
        /// <remarks>
        /// 当前为前瞻实现——实际逐 slot 接线依赖 <see cref="Pass"/> 暴露具名 slot
        /// 访问。该 API 就绪前，连接只做名字解析校验。
        /// </remarks>
        private void WireManualConnections()
        {
            foreach (SlotConnection conn in manualConnections)
            {
                if (!conn.IsValid())
                {
                    continue;
                }

                Pass source = FindPass<Pass>(conn.SourcePass);
                Pass target = FindPass<Pass>(conn.TargetPass);

                if (source == null || target == null)
                {
                    continue;
                }

                // 未来在此解析具名 slot：
                //   PassSlot sourceSlot = source.GetOutputSlot(conn.SourceSlot);
                //   PassSlot targetSlot = target.GetInputSlot(conn.TargetSlot);
                //   sourceSlot.Connect(targetSlot);
            }
        }
    }
}
