// <copyright file="RenderGraphTemplate.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HN.HNRP
{
    /// <summary>
    /// 渲染图模板标识。每个 <see cref="RenderGraphTemplate"/> 对应一个枚举值，
    /// <see cref="RenderGraphAsset"/> 序列化该值以在运行时定位到同一份构建代码。
    /// </summary>
    public enum RenderGraphKind
    {
        /// <summary>
        /// 未关联任何模板（资产尚未初始化或被删除模板）。
        /// </summary>
        None = 0,

        /// <summary>标准渲染图（StandardGraph）。</summary>
        Standard,

        /// <summary>反射渲染图（ReflectionGraph）。</summary>
        Reflection,

        /// <summary>预览渲染图（PreviewGraph）。</summary>
        Preview,
    }

    /// <summary>
    /// 渲染图模板：以<b>代码</b>定义一份固定内容的渲染图（pass/连线/settings），
    /// 并提供 Ensure 机制保证对应 <see cref="RenderGraphAsset"/> 资源在项目中存在。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 蓝图构建逻辑（<see cref="CreateBlueprint"/>）是<b>运行时与编辑器共用</b>的
    /// 唯一 pass 创建代码：编辑器用它生成/重置模板资源内容（仅 kind + settings），
    /// 运行时 <see cref="RenderGraphAsset.Build"/> 直接执行它创建 pass 实例，
    /// 不再克隆资产中的序列化 Pass（方案 X）。
    /// </para>
    /// <para>
    /// <see cref="RenderGraphAsset"/> 不再序列化 Pass/连线数据，只记录
    /// <see cref="RenderGraphKind"/> 与渲染图设置，因此 Pass 无需无参构造函数，
    /// 也无需 <c>CopyFrom</c> 全字段拷贝样板。
    /// </para>
    /// </remarks>
    public class RenderGraphTemplate
    {
        /// <summary>模板标识。</summary>
        public RenderGraphKind Kind { get; }

        /// <summary>模板/资源名称。</summary>
        public string AssetName { get; }

        /// <summary>Editor 下资产路径（Assets/.../xxx.asset）。</summary>
        public string AssetPath { get; }

        /// <summary>非 Editor 下 Resources 加载路径。</summary>
        public string ResourcesPath { get; }

        /// <summary>蓝图构建代码（运行时/编辑器共用）。</summary>
        private readonly Func<RenderGraphBlueprint> createBlueprint;

        /// <summary>缓存实例，保证唯一。</summary>
        private RenderGraphAsset cached;

        /// <summary>
        /// 初始化 <see cref="RenderGraphTemplate"/> 的新实例。
        /// </summary>
        /// <param name="kind">模板标识。</param>
        /// <param name="assetName">模板/资源名称。</param>
        /// <param name="assetPath">Editor 下资产路径。</param>
        /// <param name="resourcesPath">非 Editor 下 Resources 加载路径。</param>
        /// <param name="createBlueprint">蓝图构建代码。</param>
        public RenderGraphTemplate(
            RenderGraphKind kind,
            string assetName,
            string assetPath,
            string resourcesPath,
            Func<RenderGraphBlueprint> createBlueprint)
        {
            Kind = kind;
            AssetName = assetName;
            AssetPath = assetPath;
            ResourcesPath = resourcesPath;
            this.createBlueprint = createBlueprint;
        }

        /// <summary>
        /// 执行模板的蓝图构建代码，返回一份全新的渲染图定义
        /// （pass 均为新实例，运行时/编辑器调用互不干扰）。
        /// </summary>
        /// <returns>一份全新的 <see cref="RenderGraphBlueprint"/>。</returns>
        public RenderGraphBlueprint CreateBlueprint()
        {
            return createBlueprint();
        }

        /// <summary>
        /// 确保模板资源存在并返回唯一实例。Editor：LoadAssetAtPath → FindAssets 按名称兜底 → 创建并初始化；
        /// kind 不匹配的资源会被重新初始化；非 Editor：Resources.Load。
        /// </summary>
        public RenderGraphAsset Ensure()
        {
            if (cached == null)
            {
                cached = EnsureGraph(this);
            }
            return cached;
        }

        private static RenderGraphAsset EnsureGraph(RenderGraphTemplate template)
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<RenderGraphAsset>(template.AssetPath);
            if (asset == null)
            {
                var guids = AssetDatabase.FindAssets("t:RenderGraphAsset");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<RenderGraphAsset>(path);
                    if (candidate != null && candidate.name == template.AssetName)
                    {
                        asset = candidate;
                        break;
                    }
                }
            }

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
                asset.name = template.AssetName;
                template.ApplyToAsset(asset);
                AssetDatabase.CreateAsset(asset, template.AssetPath);
                AssetDatabase.SaveAssets();
            }
            else if (asset.Kind != template.Kind)
            {
                // 过期/遗留资产不再匹配本模板的 kind：
                // 依据模板构建代码重新初始化，使资产指向
                // 与生成其运行时渲染图相同的代码。
                template.ApplyToAsset(asset);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            return asset;
#else
            var loaded = Resources.Load<RenderGraphAsset>(template.ResourcesPath);
            if (loaded == null)
            {
                Debug.LogWarning($"HNRP: {template.AssetName} not found in Resources at '{template.ResourcesPath}'. The pipeline needs the asset to render.");
            }
            return loaded;
#endif
        }

        /// <summary>
        /// 用模板代码初始化资源：记录 kind，并把蓝图 settings 写入资源（settings 是
        /// 唯一允许在资产上保留/查看的模板级参数）。
        /// </summary>
        /// <param name="asset">目标渲染图资产。</param>
        private void ApplyToAsset(RenderGraphAsset asset)
        {
            RenderGraphBlueprint blueprint = CreateBlueprint();
            asset.SetTemplate(Kind, blueprint.Settings);
        }
    }
}
