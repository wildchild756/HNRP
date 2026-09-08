// <copyright file="RendererListParams.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace HN.HNRP
{
    /// <summary>
    /// 渲染器列表资源参数（值类型）。承载从相机裁剪结果构建渲染器列表所需的
    /// 参数，作为 Pass 的 <c>[SerializeField]</c> 字段序列化（取代旧的
    /// <see cref="ResourceDefinition"/> 体系）。
    /// </summary>
    /// <remarks>
    /// 当 Pass 输入槽未连接或句柄无效时，Pass 内部用这些参数自建渲染器列表
    /// （<c>renderGraph.CreateRendererList</c>）。
    /// </remarks>
    [Serializable]
    public struct RendererListParams
    {
        /// <summary>
        /// 构建渲染器列表使用的渲染队列范围（不透明或透明）。
        /// </summary>
        public RenderListKind ListKind;

        /// <summary>
        /// 构建渲染器列表时应用的渲染层掩码，仅匹配层上的渲染器被包含。
        /// </summary>
        public uint RenderingLayerMask;

        /// <summary>
        /// 初始化默认参数：不透明队列、渲染层掩码 <c>0x00000001</c>。
        /// </summary>
        public static RendererListParams CreateDefault()
        {
            return new RendererListParams
            {
                ListKind = RenderListKind.Opaque,
                RenderingLayerMask = 0x00000001,
            };
        }

        /// <summary>
        /// 构建渲染器列表描述符。
        /// </summary>
        /// <param name="passNames">渲染该列表的 shader pass 名。</param>
        /// <param name="cullingResults">当前帧相机裁剪结果。</param>
        /// <param name="camera">当前相机。</param>
        /// <returns>可直接传给 <c>renderGraph.CreateRendererList</c> 的描述符。</returns>
        public RendererListDesc CreateDesc(
            ShaderTagId[] passNames,
            UnityEngine.Rendering.CullingResults cullingResults,
            Camera camera)
        {
            return ListKind == RenderListKind.Opaque
                ? GetOpaqueRendererListDesc(
                    passNames, cullingResults, camera, RenderingLayerMask)
                : GetTransparentRendererListDesc(
                    passNames, cullingResults, camera, RenderingLayerMask);
        }


        public static RendererListDesc GetOpaqueRendererListDesc(ShaderTagId[] passNames, CullingResults cullingResults, Camera camera, uint renderingLayerMask)
        {
            var desc = new RendererListDesc(passNames, cullingResults, camera)
            {
                renderingLayerMask = renderingLayerMask,
                rendererConfiguration = GetPerObjectLightFlags(),
                renderQueueRange = HNRenderQueue.AllOpaque,
                sortingCriteria = SortingCriteria.CommonOpaque,
                stateBlock = null,
                overrideMaterial = null,
                excludeObjectMotionVectors = false,
            };

            return desc;
        }

        public static RendererListDesc GetTransparentRendererListDesc(ShaderTagId[] passNames, CullingResults cullingResults, Camera camera, uint renderingLayerMask)
        {
            var desc = new RendererListDesc(passNames, cullingResults, camera)
            {
                renderingLayerMask = renderingLayerMask,
                rendererConfiguration = GetPerObjectLightFlags(),
                renderQueueRange = HNRenderQueue.Transparent,
                sortingCriteria = SortingCriteria.CommonTransparent,
                stateBlock = null,
                overrideMaterial = null,
                excludeObjectMotionVectors = false,
            };

            return desc;
        }

        public static PerObjectData GetPerObjectLightFlags()
        {
            var configuration =
                PerObjectData.Lightmaps
                | PerObjectData.LightProbe
                | PerObjectData.OcclusionProbe
                | PerObjectData.ShadowMask
                | PerObjectData.ReflectionProbes
                | PerObjectData.LightData
                ;

            return configuration;
        }


    }

    /// <summary>
    /// 渲染器列表的渲染队列范围。
    /// </summary>
    public enum RenderListKind
    {
        /// <summary>
        /// 不透明渲染队列范围，按不透明排序规则排序。
        /// </summary>
        Opaque,

        /// <summary>
        /// 透明渲染队列范围，从远到近排序。
        /// </summary>
        Transparent,
    }
}
