// <copyright file="RenderGraphTemplates.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 渲染图模板注册表。每个模板以<b>代码</b>定义渲染图蓝图（pass 构造代码 +
    /// 连线 + settings），运行时与编辑器共用同一份构造代码。未来扩展新模板：
    /// 新增 <see cref="RenderGraphKind"/> 枚举值 + 本文件新增一个静态模板实例即可，
    /// 无需改动 <see cref="RenderGraphAsset"/>。
    /// </summary>
    /// <remarks>
    /// 渲染资源由各 Pass 自行分配：Pass 消费已连接的输入槽，未连接/无效时
    /// 用自身参数创建资源（ADR-017）。
    /// </remarks>
    public static class RenderGraphTemplates
    {
        /// <summary>标准渲染图模板（8 pass / 18 slot，PerPixel+HDR）。</summary>
        public static readonly RenderGraphTemplate Standard = new RenderGraphTemplate(
            RenderGraphKind.Standard,
            "StandardGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/StandardGraph.asset",
            "RenderGraphs/StandardGraph",
            CreateStandardGraph);

        /// <summary> 反射渲染图模板（7 pass / 11 slot，PerPixel+HDR）。</summary>
        public static readonly RenderGraphTemplate Reflection = new RenderGraphTemplate(
            RenderGraphKind.Reflection,
            "ReflectionGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/ReflectionGraph.asset",
            "RenderGraphs/ReflectionGraph",
            CreateReflectionGraph);

        /// <summary>预览渲染图模板（2 pass / 1 slot，PerPixel+HDR）。</summary>
        public static readonly RenderGraphTemplate Preview = new RenderGraphTemplate(
            RenderGraphKind.Preview,
            "PreviewGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/PreviewGraph.asset",
            "RenderGraphs/PreviewGraph",
            CreatePreviewGraph);

        /// <summary>kind → 模板实例 映射，供 <see cref="RenderGraphAsset"/> 按标识定位构建代码。</summary>
        private static readonly Dictionary<RenderGraphKind, RenderGraphTemplate> templatesByKind = new()
        {
            { RenderGraphKind.Standard, Standard },
            { RenderGraphKind.Reflection, Reflection },
            { RenderGraphKind.Preview, Preview },
        };

        /// <summary>
        /// 按模板标识取回模板实例；未注册或 <see cref="RenderGraphKind.None"/> 时返回 <c>null</c>。
        /// </summary>
        /// <param name="kind">模板标识。</param>
        /// <returns>对应的 <see cref="RenderGraphTemplate"/>，未注册时为 <c>null</c>。</returns>
        public static RenderGraphTemplate Get(RenderGraphKind kind)
        {
            templatesByKind.TryGetValue(kind, out RenderGraphTemplate template);
            return template;
        }

        /// <summary>确保所有模板资源存在（Editor 下创建，非 Editor 下 Resources.Load）。</summary>
        public static void EnsureAll()
        {
            Standard.Ensure();
            Reflection.Ensure();
            Preview.Ensure();
        }

        private static RenderGraphBlueprint CreateStandardGraph()
        {
            return new RenderGraphBlueprint(
                new List<Pass>
                {
                    new BuildLightDataPass("buildLight"),
                    new DrawShadowPass("drawShadow"),
                    new ClusterCullingReflectionProbePass("clusterProbe"),
                    new ClusterCullingLightPass("clusterLight"),
                    new DrawObjectPass("forwardOpaque"),
                    new BuiltinSkyPass("sky"),
                    new DrawObjectPass("transparency")
                    {
                        RendererListParams = new RendererListParams
                        {
                            ListKind = RenderListKind.Transparent,
                            RenderingLayerMask = 0x00000001,
                        },
                    },
                    new EditorWireOverlayPass("wireOverlay"),
                    new RenderOutputPass("finalBlit"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create("forwardOpaque", "ColorTargetOutput", "sky", "ColorTarget"),
                    SlotConnection.Create("forwardOpaque", "DepthTargetOutput", "sky", "DepthTarget"),
                    SlotConnection.Create("sky", "ColorTargetOutput", "transparency", "ColorTarget"),
                    SlotConnection.Create("sky", "DepthTargetOutput", "transparency", "DepthTarget"),
                    SlotConnection.Create("transparency", "ColorTargetOutput", "wireOverlay", "ColorTarget"),
                    SlotConnection.Create("transparency", "ColorTargetOutput", "finalBlit", "ColorTarget"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "forwardOpaque", "LightDatas"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "transparency", "LightDatas"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "drawShadow", "lightDatasBuffer"),
                    SlotConnection.Create("drawShadow", "ShadowMap", "forwardOpaque", "ShadowMap"),
                    SlotConnection.Create("drawShadow", "ShadowMap", "transparency", "ShadowMap"),
                    SlotConnection.Create("clusterProbe", "reflectionProbeAtlasOutput", "transparency", "ReflectionProbeAtlas"),
                    SlotConnection.Create("clusterProbe", "clusterCullingReflectionProbeMaskBuffer", "transparency", "ProbeMask"),
                    SlotConnection.Create("clusterProbe", "clusterCullingReflectionProbeDatasBuffer", "transparency", "ProbeDatas"),
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "transparency", "LightMask"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "clusterLight", "lightDatasBuffer"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "forwardOpaque", "LightDatas"),
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "forwardOpaque", "LightMask"),
                    SlotConnection.Create("clusterProbe", "reflectionProbeAtlasOutput", "forwardOpaque", "ReflectionProbeAtlas"),
                    SlotConnection.Create("clusterProbe", "clusterCullingReflectionProbeMaskBuffer", "forwardOpaque", "ProbeMask"),
                    SlotConnection.Create("clusterProbe", "clusterCullingReflectionProbeDatasBuffer", "forwardOpaque", "ProbeDatas"),
                },
                new RenderGraphSettings
                {
                    SHEvalMode = SHEvalMode.PerPixel,
                    AllowHDR = true,
                });
        }

        private static RenderGraphBlueprint CreateReflectionGraph()
        {
            return new RenderGraphBlueprint(
                new List<Pass>
                {
                    new BuildLightDataPass("buildLight"),
                    new DrawShadowPass("drawShadow"),
                    new ClusterCullingLightPass("clusterLight"),
                    new DrawObjectPass("forwardOpaque"),
                    new BuiltinSkyPass("sky"),
                    new DrawObjectPass("transparency")
                    {
                        RendererListParams = new RendererListParams
                        {
                            ListKind = RenderListKind.Transparent,
                            RenderingLayerMask = 0x00000001,
                        },
                    },
                    new EditorWireOverlayPass("wireOverlay"),
                    new RenderOutputPass("finalBlit"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create("forwardOpaque", "ColorTargetOutput", "sky", "ColorTarget"),
                    SlotConnection.Create("forwardOpaque", "DepthTargetOutput", "sky", "DepthTarget"),
                    SlotConnection.Create("sky", "ColorTargetOutput", "transparency", "ColorTarget"),
                    SlotConnection.Create("sky", "DepthTargetOutput", "transparency", "DepthTarget"),
                    SlotConnection.Create("transparency", "ColorTargetOutput", "wireOverlay", "ColorTarget"),
                    SlotConnection.Create("transparency", "ColorTargetOutput", "finalBlit", "ColorTarget"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "forwardOpaque", "LightDatas"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "transparency", "LightDatas"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "drawShadow", "lightDatasBuffer"),
                    SlotConnection.Create("drawShadow", "ShadowMap", "forwardOpaque", "ShadowMap"),
                    SlotConnection.Create("drawShadow", "ShadowMap", "transparency", "ShadowMap"),
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "transparency", "LightMask"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "clusterLight", "lightDatasBuffer"),
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "forwardOpaque", "LightMask"),
                },
                new RenderGraphSettings
                {
                    SHEvalMode = SHEvalMode.PerPixel,
                    AllowHDR = true,
                });
        }

        private static RenderGraphBlueprint CreatePreviewGraph()
        {
            return new RenderGraphBlueprint(
                new List<Pass>
                {
                    new DrawObjectPass("opaque"),
                    new RenderOutputPass("finalBlit"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create("opaque", "ColorTargetOutput", "finalBlit", "ColorTarget"),
                },
                new RenderGraphSettings
                {
                    SHEvalMode = SHEvalMode.PerPixel,
                    AllowHDR = true,
                });
        }
    }
}
