// <copyright file="RenderGraphBuilder.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// 把一份 <see cref="RenderGraphBlueprint"/> 构建为运行时可执行的
    /// <see cref="Pass"/> 列表：声明 slot、按名称连线、拓扑排序、过滤禁用 pass。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 构建逻辑与蓝图来源解耦：蓝图由 <see cref="RenderGraphTemplate"/> 的构建代码
    /// 提供（运行时与编辑器同一份代码），本类只负责把蓝图实例化为就绪的 pass 图。
    /// </para>
    /// </remarks>
    public static class RenderGraphBuilder
    {
        /// <summary>
        /// 从蓝图构建可执行的 pass 列表。
        /// </summary>
        /// <param name="blueprint">
        /// 待声明与连线的蓝图。其 pass 实例会被<b>直接消费</b>（不做克隆）——
        /// 需要按相机隔离时，调用方须传入新构建的蓝图。
        /// </param>
        /// <returns>
        /// 包含全部启用 pass 的稳定拓扑有序 <see cref="List{Pass}"/>；
        /// 蓝图为空时返回空列表。
        /// </returns>
        public static List<Pass> Build(RenderGraphBlueprint blueprint)
        {
            if (blueprint == null)
            {
                return new List<Pass>();
            }

            var passMap = new Dictionary<string, Pass>();

            // ── 阶段 1：为每个蓝图 pass 实例声明 slot ──
            foreach (Pass pass in blueprint.Passes)
            {
                if (pass == null)
                {
                    Debug.LogWarning("RenderGraphBuilder.Build: 跳过空 pass。");
                    continue;
                }

                if (string.IsNullOrEmpty(pass.PassName))
                {
                    Debug.LogWarning(
                        $"RenderGraphBuilder.Build: 跳过 PassName 为 null/空 的 pass " +
                        $"({pass.GetType().Name})。");
                    continue;
                }

                // 一次性声明 slot，使阶段 2 连线的 slot 实例与每帧 Record
                // 使用的实例一致。
                pass.SetupSlots();

                passMap[pass.PassName] = pass;
            }

            // ── 阶段 2：按名称连线 slot ──
            foreach (SlotConnection conn in blueprint.Connections)
            {
                if (!conn.IsValid())
                {
                    Debug.LogWarning(
                        $"RenderGraphBuilder.Build: 跳过无效 SlotConnection " +
                        $"(SourcePass='{conn.SourcePass}', SourceSlot='{conn.SourceSlot}', " +
                        $"TargetPass='{conn.TargetPass}', TargetSlot='{conn.TargetSlot}')。");
                    continue;
                }

                if (!passMap.TryGetValue(conn.SourcePass, out Pass sourcePass))
                {
                    Debug.LogWarning(
                        $"RenderGraphBuilder.Build: SlotConnection 引用了未知的 SourcePass " +
                        $"'{conn.SourcePass}'。");
                    continue;
                }

                if (!passMap.TryGetValue(conn.TargetPass, out Pass targetPass))
                {
                    Debug.LogWarning(
                        $"RenderGraphBuilder.Build: SlotConnection 引用了未知的 TargetPass " +
                        $"'{conn.TargetPass}'。");
                    continue;
                }

                ConnectPassSlots(sourcePass, conn.SourceSlot, targetPass, conn.TargetSlot);
            }

            // ── 阶段 3：拓扑排序（依赖顺序） ──
            return TopologicalSort(blueprint.Passes, blueprint.Connections, passMap);
        }

        /// <summary>
        /// 对所有构建出的 pass 拓扑排序，使依赖边被遵守，只返回启用状态的 pass。
        /// </summary>
        /// <param name="definitionOrder">
        /// 蓝图的声明顺序，作为稳定的次序破平依据。
        /// </param>
        /// <param name="connections">slot 连接边。</param>
        /// <param name="passMap">阶段 1 中声明的 pass，按实例名索引。</param>
        /// <returns>
        /// 稳定拓扑有序的启用 pass；检测到环时按声明顺序返回全部启用 pass。
        /// </returns>
        private static List<Pass> TopologicalSort(
            List<Pass> definitionOrder,
            List<SlotConnection> connections,
            Dictionary<string, Pass> passMap)
        {
            // 稳定基准顺序：声明顺序，仅保留实际构建成功的 pass
            // （passMap 可能省略构建失败的 pass）。
            var order = new List<Pass>(passMap.Count);
            var index = new Dictionary<Pass, int>();
            foreach (Pass pass in definitionOrder)
            {
                if (pass == null || string.IsNullOrEmpty(pass.PassName))
                {
                    continue;
                }

                if (passMap.TryGetValue(pass.PassName, out Pass built))
                {
                    index[built] = order.Count;
                    order.Add(built);
                }
            }

            int count = order.Count;
            var adjacency = new Dictionary<int, HashSet<int>>();
            var inDegree = new int[count];

            // ── 从 SlotConnection 构造依赖边 ──
            foreach (SlotConnection conn in connections)
            {
                if (!conn.IsValid())
                {
                    continue;
                }

                if (!passMap.TryGetValue(conn.SourcePass, out Pass source)
                    || !passMap.TryGetValue(conn.TargetPass, out Pass target)
                    || source == target)
                {
                    continue;
                }

                if (index.TryGetValue(source, out int sourceIndex)
                    && index.TryGetValue(target, out int targetIndex))
                {
                    AddEdge(adjacency, inDegree, sourceIndex, targetIndex);
                }
            }

            // ── Kahn 算法，使用稳定（声明顺序）破平 ──
            var result = new List<Pass>(count);
            var visited = new bool[count];
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                for (int i = 0; i < count; i++)
                {
                    if (visited[i] || inDegree[i] != 0)
                    {
                        continue;
                    }

                    visited[i] = true;
                    result.Add(order[i]);
                    progressed = true;

                    if (adjacency.TryGetValue(i, out HashSet<int> targets))
                    {
                        foreach (int target in targets)
                        {
                            if (!visited[target])
                            {
                                inDegree[target]--;
                            }
                        }
                    }

                    break;
                }
            }

            // ── 环检测：追加剩余节点，保证不丢 pass ──
            if (result.Count < count)
            {
                Debug.LogWarning(
                    $"RenderGraphBuilder.TopologicalSort: 渲染图存在环。" +
                    $"按声明顺序追加剩余 pass。");
                for (int i = 0; i < count; i++)
                {
                    if (!visited[i])
                    {
                        result.Add(order[i]);
                    }
                }
            }

            // ── 只保留启用状态的 pass ──
            var enabled = new List<Pass>(result.Count);
            foreach (Pass pass in result)
            {
                if (pass.IsEnabled)
                {
                    enabled.Add(pass);
                }
            }

            return enabled;
        }

        /// <summary>
        /// 添加从索引 <paramref name="from"/> 到 <paramref name="to"/> 的依赖边，
        /// 并行边去重。
        /// </summary>
        private static void AddEdge(
            Dictionary<int, HashSet<int>> adjacency,
            int[] inDegree,
            int from,
            int to)
        {
            if (!adjacency.TryGetValue(from, out HashSet<int> targets))
            {
                targets = new HashSet<int>();
                adjacency[from] = targets;
            }

            if (targets.Add(to))
            {
                inDegree[to]++;
            }
        }

        /// <summary>
        /// 把 <paramref name="source"/> 的输出 slot 与 <paramref name="target"/>
        /// 的输入 slot 按名称相连。
        /// </summary>
        /// <param name="source">提供输出 slot 的源 pass。</param>
        /// <param name="sourceSlot">源 pass 上的输出 slot 名。</param>
        /// <param name="target">提供输入 slot 的目标 pass。</param>
        /// <param name="targetSlot">目标 pass 上的输入 slot 名。</param>
        /// <remarks>
        /// 方向不匹配或缺失 slot 时静默失败。连接成功后，目标输入 slot 的
        /// <see cref="PassSlot.IsConnected"/> 为 <c>true</c>，目标 pass 在
        /// <see cref="Pass.Record"/> 中读取源 pass 的资源句柄。
        /// </remarks>
        private static void ConnectPassSlots(
            Pass source,
            string sourceSlot,
            Pass target,
            string targetSlot)
        {
            source.TryConnect(sourceSlot, target, targetSlot);
        }
    }
}
