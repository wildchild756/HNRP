// <copyright file="RenderGraphAsset.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// 光照 pass 使用的球谐（SH）求值模式。
    /// </summary>
    public enum SHEvalMode
    {
        /// <summary>
        /// 逐顶点求值球谐（SH）。
        /// </summary>
        PerVertex,

        /// <summary>
        /// 逐顶点与逐像素混合的球谐（SH）求值。
        /// </summary>
        Mixed,

        /// <summary>
        /// 逐像素求值球谐（SH）。
        /// </summary>
        PerPixel,
    }

    /// <summary>
    /// 可序列化结构体：每个资产级渲染图设置。
    /// 对应影响 pass 执行方式的顶层设置。
    /// </summary>
    [Serializable]
    public struct RenderGraphSettings
    {
        /// <summary>
        /// 光照 pass 使用的球谐求值模式。
        /// </summary>
        public SHEvalMode SHEvalMode;

        /// <summary>
        /// 为 <c>true</c> 时渲染图可能分配 HDR 渲染目标；
        /// 为 <c>false</c> 时全部目标为 LDR。
        /// </summary>
        public bool AllowHDR;
    }

    /// <summary>
    /// 渲染图模板 <see cref="ScriptableObject"/>。
    /// 指向某个 <see cref="RenderGraphKind"/>，其 <see cref="RenderGraphTemplate"/>
    /// 构建代码是该图 pass 列表与连线的唯一数据源。资产本身只保存 kind 与
    /// 渲染图级设置（运行时 pass 的 PreRecord 读取该设置）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>架构说明</b>（ADR-002、ADR-011、方案 X）：资产是轻量模板标记——
    /// 不再序列化 <see cref="Pass"/> 实例（已移除 <c>[SerializeReference]</c>）。
    /// 编辑器（重新生成模板资产时）与运行时（<see cref="Build"/>）都执行同一份
    /// 模板构建代码（<see cref="RenderGraphTemplate.CreateBlueprint"/>）来创建 pass，
    /// 因此 pass 定义保持为纯 C# 类，无需无参构造或 <c>CopyFrom</c> 样板。
    /// </para>
    /// <para>
    /// 相机直接引用 <see cref="RenderGraphAsset"/> —— 或通过
    /// <see cref="HNAdditionalCameraData.pipelineConfigOverride"/>，
    /// 或通过 <see cref="HNRenderPipelineAsset"/> 上的默认渲染图字段
    /// （如 <c>DefaultGameRenderGraph</c>）。
    /// </para>
    /// </remarks>
    public class RenderGraphAsset : ScriptableObject
    {
        [SerializeField]
        private RenderGraphKind kind;

        [SerializeField]
        private RenderGraphSettings settings;

        /// <summary>
        /// 按 pass 实例名索引的参数缓存（L1 编辑器保存层）。
        /// 与模板创建代码生成的运行时 pass 为<b>同类型</b>的另一个实例，仅承载
        /// 参数值，不参与渲染；运行时 <see cref="Build"/> 把缓存值注入到模板
        /// 代码创建的 pass 上（模板默认 → 编辑器保存值，逐级覆盖）。
        /// 空缓存 = 使用模板代码默认参数。
        /// </summary>
        [SerializeField, SerializeReference]
        private List<Pass> passParameterCache = new();

        /// <summary>
        /// 参数缓存修订号。任何对参数缓存或渲染图设置的成功修改都会自增，
        /// 供运行时判断是否需要重建已构建的 pass 列表。
        /// 仅比较模板引用相等无法感知同一资源上的参数改动，故用修订号兜底。
        /// </summary>
        [SerializeField]
        private int parameterRevision;

        /// <summary>
        /// 获取本资产指向的模板标识。被序列化，使运行时（Resources.Load）
        /// 能与编辑器解析到同一份构建代码。
        /// </summary>
        public RenderGraphKind Kind => kind;

        /// <summary>
        /// 获取或设置渲染图设置。写入时自增参数修订号，使运行时重建 pass 列表。
        /// </summary>
        public RenderGraphSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                parameterRevision++;
            }
        }

        /// <summary>
        /// 获取参数缓存修订号。修改参数缓存 / 设置后自增，运行时据此判断
        /// 是否重建已构建的 pass 列表。
        /// </summary>
        public int ParameterRevision => parameterRevision;

        /// <summary>
        /// 自增参数修订号。供编辑器在经 <see cref="UnityEditor.SerializedObject"/>
        /// 提交参数缓存改动后调用（该路径不经过本类的公共写方法）。
        /// </summary>
        public void BumpParameterRevision()
        {
            parameterRevision++;
        }

        /// <summary>
        /// 关联模板并写入模板级设置。由 <see cref="RenderGraphTemplate"/> 在创建 /
        /// 重置模板资源时调用（蓝图 settings 来自同一份构建代码）。
        /// </summary>
        /// <param name="templateKind">模板标识。</param>
        /// <param name="templateSettings">模板蓝图设置。</param>
        public void SetTemplate(RenderGraphKind templateKind, RenderGraphSettings templateSettings)
        {
            kind = templateKind;
            settings = templateSettings;
            parameterRevision++;
        }

        /// <summary>
        /// 获取全部参数缓存 Pass（L1 编辑器保存层）。每项与模板 pass 同类型，
        /// 通过 <see cref="Pass.PassName"/> 对应到模板代码创建的同名实例。
        /// </summary>
        public IReadOnlyList<Pass> PassParameterCache => passParameterCache;

        /// <summary>
        /// 按类型与实例名查找参数缓存；没有对应缓存时返回 <c>null</c>。
        /// </summary>
        /// <param name="passType">模板 pass 的具体类型。</param>
        /// <param name="passName">模板 pass 的实例名。</param>
        /// <returns>匹配的参数缓存 Pass，未找到时为 <c>null</c>。</returns>
        public Pass GetParameterOverride(System.Type passType, string passName)
        {
            foreach (Pass cached in passParameterCache)
            {
                if (cached != null
                    && cached.GetType() == passType
                    && cached.PassName == passName)
                {
                    return cached;
                }
            }

            return null;
        }

        /// <summary>
        /// 是否已存在匹配类型与实例名的参数缓存。
        /// </summary>
        /// <param name="passType">模板 pass 的具体类型。</param>
        /// <param name="passName">模板 pass 的实例名。</param>
        /// <returns>存在时返回 <c>true</c>，否则 <c>false</c>。</returns>
        public bool HasParameterOverride(System.Type passType, string passName)
        {
            return GetParameterOverride(passType, passName) != null;
        }

        /// <summary>
        /// 添加（或替换同名同类型已有的）参数缓存条目。缓存承载与模板 pass
        /// 相同的可序列化参数；<see cref="Build"/> 会把缓存值注入同名运行时 pass。
        /// </summary>
        /// <param name="parameterOverride">
        /// 参数缓存 Pass 实例（与目标模板 pass 同类型、同实例名）。不能为 <c>null</c>。
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// 当 <paramref name="parameterOverride"/> 为 <c>null</c> 时抛出。
        /// </exception>
        public void AddParameterOverride(Pass parameterOverride)
        {
            if (parameterOverride == null)
            {
                throw new System.ArgumentNullException(nameof(parameterOverride));
            }

            RemoveParameterOverride(parameterOverride.GetType(), parameterOverride.PassName);
            passParameterCache.Add(parameterOverride);
            parameterRevision++;
        }

        /// <summary>
        /// 移除匹配类型与实例名的参数缓存条目（回到模板代码默认参数）。
        /// </summary>
        /// <param name="passType">模板 pass 的具体类型。</param>
        /// <param name="passName">模板 pass 的实例名。</param>
        /// <returns>存在并移除时返回 <c>true</c>，否则 <c>false</c>。</returns>
        public bool RemoveParameterOverride(System.Type passType, string passName)
        {
            for (int i = 0; i < passParameterCache.Count; i++)
            {
                Pass cached = passParameterCache[i];
                if (cached != null
                    && cached.GetType() == passType
                    && cached.PassName == passName)
                {
                    passParameterCache.RemoveAt(i);
                    parameterRevision++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 通过执行本资产对应模板的构建代码（与编辑器相同的代码）构建运行时
        /// <see cref="Pass"/> 列表。每次调用都会产生全新的 pass 实例，并由
        /// <see cref="RenderGraphBuilder"/> 完成声明、连线与排序；只返回启用 pass。
        /// </summary>
        /// <returns>
        /// 包含全部启用 pass 的新 <see cref="List{Pass}"/>；
        /// 未配置模板标识时返回空列表。
        /// </returns>
        public List<Pass> Build()
        {
            RenderGraphTemplate template = RenderGraphTemplates.Get(kind);
            if (template == null)
            {
                Debug.LogWarning(
                    $"RenderGraphAsset.Build: '{name}' 没有匹配的 RenderGraphTemplate " +
                    $"(kind='{kind}')，回退为空 pass 列表。");
                return new List<Pass>();
            }

            RenderGraphBlueprint blueprint = template.CreateBlueprint();
            List<Pass> passes = RenderGraphBuilder.Build(blueprint);

            ApplyParameterOverrides(passes);
            return passes;
        }

        /// <summary>
        /// 把参数缓存（L1 编辑器保存层）注入构建出的运行时 pass：
        /// 对每个 pass，存在同类型同名缓存时，把缓存参数拷到运行时实例上。
        /// 无缓存时保留模板代码默认参数（L0）。运行时动态修改（L2）发生在
        /// 本方法返回后的 pass 实例上，不写回本缓存。
        /// </summary>
        /// <param name="passes">模板代码构建出的运行时 pass 列表。</param>
        private void ApplyParameterOverrides(List<Pass> passes)
        {
            if (passParameterCache == null || passParameterCache.Count == 0)
            {
                return;
            }

            foreach (Pass pass in passes)
            {
                if (pass == null)
                {
                    continue;
                }

                Pass cache = GetParameterOverride(pass.GetType(), pass.PassName);
                if (cache != null)
                {
                    PassParameterCopy.CopyParameters(cache, pass);
                }
            }
        }
    }
}
