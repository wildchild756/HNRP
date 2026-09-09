// <copyright file="RenderGraphBlueprint.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;

namespace HN.HNRP
{
    /// <summary>
    /// 一份渲染图的完整代码定义：pass 列表 + slot 连接 + 渲染图设置。
    /// 由 <see cref="RenderGraphTemplate"/> 的构建代码产生 —— 编辑器填充模板资源
    /// 与运行时 <see cref="RenderGraphAsset.Build"/> 共用同一份构建代码（方案 X）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RenderGraphBlueprint"/> 只是数据载体：构造后即拥有一批全新的
    /// <see cref="Pass"/> 实例。每次调用模板构建器都会得到独立实例，
    /// 因此运行时与编辑器对同一模板拿到的是互不干扰的对象。
    /// </para>
    /// </remarks>
    public sealed class RenderGraphBlueprint
    {
        /// <summary>
        /// 获取本蓝图声明的有序 pass 实例列表。
        /// </summary>
        public List<Pass> Passes { get; }

        /// <summary>
        /// 获取把各 pass 连接起来的有序 slot 连接列表。
        /// </summary>
        public List<SlotConnection> Connections { get; }

        /// <summary>
        /// 获取本蓝图声明的渲染图设置。
        /// </summary>
        public RenderGraphSettings Settings { get; }

        /// <summary>
        /// 初始化 <see cref="RenderGraphBlueprint"/> 的新实例。
        /// </summary>
        /// <param name="passes">本渲染图的有序 pass 实例。</param>
        /// <param name="connections">连接各 pass 的 slot 连接。</param>
        /// <param name="settings">渲染图级设置。</param>
        public RenderGraphBlueprint(
            List<Pass> passes,
            List<SlotConnection> connections,
            RenderGraphSettings settings)
        {
            Passes = passes;
            Connections = connections;
            Settings = settings;
        }
    }
}
