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
    /// 渲染图模板注册表。未来扩展新模板：在此文件新增一个静态模板实例 + 对应填充方法即可，
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
            "StandardGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/StandardGraph.asset",
            "RenderGraphs/StandardGraph",
            PopulateStandardGraph);

        /// <summary> 反射渲染图模板（7 pass / 11 slot，PerPixel+HDR）。</summary>
        public static readonly RenderGraphTemplate Reflection = new RenderGraphTemplate(
            "ReflectionGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/ReflectionGraph.asset",
            "RenderGraphs/ReflectionGraph",
            PopulateReflectionGraph);

        /// <summary>预览渲染图模板（2 pass / 1 slot，PerVertex+无HDR）。</summary>
        public static readonly RenderGraphTemplate Preview = new RenderGraphTemplate(
            "PreviewGraph",
            HNRenderPipelineGlobalSettings.HNRenderPipelinePath + "Runtime/Resources/RenderGraphs/PreviewGraph.asset",
            "RenderGraphs/PreviewGraph",
            PopulatePreviewGraph);

        // 未来扩展示例：
        // public static readonly RenderGraphTemplate Xxx = new RenderGraphTemplate(...);

        /// <summary>确保所有模板资源存在（Editor 下创建，非 Editor 下 Resources.Load）。</summary>
        public static void EnsureAll()
        {
            Standard.Ensure();
            Reflection.Ensure();
            Preview.Ensure();
        }

        private static void PopulateStandardGraph(RenderGraphAsset g)
        {
            g.SetDefinition(
                new List<Pass>
                {
                    new BuildLightDataPass("buildLight"),
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
                }
            );
        }

        private static void PopulateReflectionGraph(RenderGraphAsset g)
        {
            g.SetDefinition(
                new List<Pass>
                {
                    new BuildLightDataPass("buildLight"),
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
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "transparency", "LightMask"),
                    SlotConnection.Create("buildLight", "lightDatasBuffer", "clusterLight", "lightDatasBuffer"),
                    SlotConnection.Create("clusterLight", "clusterCullingLightMaskBuffer", "forwardOpaque", "LightMask"),
                },
                new RenderGraphSettings 
                { 
                    SHEvalMode = SHEvalMode.PerPixel, 
                    AllowHDR = true,
                }
            );
        }

        private static void PopulatePreviewGraph(RenderGraphAsset g)
        {
            g.SetDefinition(
                new List<Pass>
                {
                    new DrawObjectPass("opaque") { SetLightGlobals = false },
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
                }
            );
        }
    }
}
