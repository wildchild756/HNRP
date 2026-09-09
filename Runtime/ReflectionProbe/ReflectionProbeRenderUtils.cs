// <copyright file="ReflectionProbeRenderUtils.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 实时反射探针渲染的纯辅助逻辑：时间切片面调度、实时模式过滤，
    /// 以及从 <see cref="VisibleReflectionProbe"/> 剔除数据中查找反射探针。
    /// </summary>
    public static class ReflectionProbeRenderUtils
    {
        /// <summary>
        /// 六个 cubemap 面索引（<c>0..5</c>）。
        /// </summary>
        public static readonly int[] AllFaces = { 0, 1, 2, 3, 4, 5 };

        private static readonly int[] emptyFaces = Array.Empty<int>();

        /// <summary>
        /// <see cref="VisibleReflectionProbe"/> 的 <c>m_InstanceId</c> 后备字段。
        /// Unity 2022.3 未在该结构体上暴露任何公共成员；实例 id 通过反射
        /// 读取一次并缓存。
        /// </summary>
        private static readonly FieldInfo instanceIdField =
            typeof(VisibleReflectionProbe).GetField(
                "m_InstanceId",
                BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// 根据探针的时间切片模式，计算本帧应渲染哪些 cubemap 面。
        /// </summary>
        /// <param name="mode">探针的时间切片模式。</param>
        /// <param name="probeInstanceId">探针实例 id（相位偏移）。</param>
        /// <param name="frameCount">当前帧计数。</param>
        /// <param name="faceProgress">用于
        /// <see cref="ReflectionProbeTimeSlicingMode.IndividualFaces"/> 的当前面进度。</param>
        /// <returns>本帧需渲染的面索引（无面时返回空数组）。</returns>
        /// <remarks>
        /// <list type="bullet">
        /// <item><see cref="ReflectionProbeTimeSlicingMode.AllFacesAtOnce"/> ——
        /// 每六帧一次性渲染全部六个面；相位按探针实例 id 偏移，
        /// 使不同探针在不同帧刷新。</item>
        /// <item><see cref="ReflectionProbeTimeSlicingMode.IndividualFaces"/> ——
        /// 每帧只渲染一个面，在 0..5 间轮转。</item>
        /// <item><see cref="ReflectionProbeTimeSlicingMode.NoTimeSlicing"/> ——
        /// 每帧渲染全部六个面。</item>
        /// </list>
        /// </remarks>
        public static int[] GetFacesToRender(
            ReflectionProbeTimeSlicingMode mode,
            int probeInstanceId,
            int frameCount,
            int faceProgress)
        {
            switch (mode)
            {
                case ReflectionProbeTimeSlicingMode.IndividualFaces:
                    return new[] { Mathf.Abs(faceProgress) % 6 };

                case ReflectionProbeTimeSlicingMode.NoTimeSlicing:
                    return AllFaces;

                case ReflectionProbeTimeSlicingMode.AllFacesAtOnce:
                    int phase = Mathf.Abs(probeInstanceId) % 6;
                    if ((frameCount + phase) % 6 == 0)
                    {
                        return AllFaces;
                    }

                    return emptyFaces;

                default:
                    return emptyFaces;
            }
        }

        /// <summary>
        /// 在一个面渲染完成后推进逐面进度计数器。
        /// </summary>
        /// <param name="faceProgress">当前进度计数器。</param>
        /// <returns>下一个进度计数器值。</returns>
        public static int AdvanceIndividualFace(int faceProgress)
        {
            return faceProgress + 1;
        }

        /// <summary>
        /// 返回给定探针是否为实时探针（<see cref="ReflectionProbeMode.Realtime"/>）。
        /// </summary>
        /// <param name="probe">要检查的反射探针。</param>
        /// <returns>探针为实时模式时返回 <c>true</c>。</returns>
        public static bool IsRealtimeProbe(ReflectionProbe probe)
        {
            return probe != null && probe.mode == ReflectionProbeMode.Realtime;
        }

        public static bool IsBakedProbe(ReflectionProbe probe)
        {
            return probe != null && probe.mode == ReflectionProbeMode.Baked;
        }

        public static bool IsCustomBakedProbe(ReflectionProbe probe)
        {
            return probe != null && probe.mode == ReflectionProbeMode.Custom;
        }

        /// <summary>
        /// 从 <see cref="VisibleReflectionProbe"/> 剔除条目读取探针实例 id。
        /// Unity 2022.3 未在该结构体上暴露公共字段，因此通过缓存反射读取
        /// 私有 <c>m_InstanceId</c> 字段。
        /// </summary>
        /// <param name="visibleProbe">可见反射探针条目。</param>
        /// <returns>反射探针实例 id；不可用时返回 <c>0</c>。</returns>
        public static int GetProbeInstanceId(in VisibleReflectionProbe visibleProbe)
        {
            if (instanceIdField == null)
            {
                return 0;
            }

            return (int)instanceIdField.GetValue(visibleProbe);
        }

        /// <summary>
        /// 从 <see cref="VisibleReflectionProbe"/> 剔除条目获取
        /// <see cref="ReflectionProbe"/> 组件。
        /// </summary>
        /// <param name="visibleProbe">可见反射探针条目。</param>
        /// <returns>探针组件；无法解析时返回 <c>null</c>。</returns>
        public static ReflectionProbe GetReflectionProbe(in VisibleReflectionProbe visibleProbe)
        {
            int instanceId = GetProbeInstanceId(visibleProbe);
            if (instanceId == 0)
            {
                return null;
            }

            return UnityEngine.Resources.InstanceIDToObject(instanceId) as ReflectionProbe;
        }

        /// <summary>
        /// 获取 cubemap 面索引对应的世界空间旋转
        /// （<c>0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z</c>），使用 Unity 采用的
        /// OpenGL 风格 cubemap 面约定。
        /// </summary>
        /// <param name="face">取值 <c>0..5</c> 的 cubemap 面索引。</param>
        /// <returns>该面的相机旋转。</returns>
        public static Quaternion GetFaceRotation(int face)
        {
            int index = Mathf.Clamp(face, 0, 5);
            return faceRotations[index];
        }

        private static readonly Quaternion[] faceRotations =
        {
            Quaternion.LookRotation(Vector3.right, Vector3.up),     // +X
            Quaternion.LookRotation(Vector3.left, Vector3.up),      // -X
            Quaternion.LookRotation(Vector3.up, Vector3.back),      // +Y
            Quaternion.LookRotation(Vector3.down, Vector3.forward), // -Y
            Quaternion.LookRotation(Vector3.forward, Vector3.up),   // +Z
            Quaternion.LookRotation(Vector3.back, Vector3.up),      // -Z
        };

        /// <summary>
        /// 选择用于渲染给定探针的 <see cref="RenderGraphAsset"/>，
        /// 遵循探针的 <see cref="HNAdditionalReflectionProbeData.RenderGraphViewIndex"/>。
        /// 当索引越界或探针没有附加数据时，回退到反射渲染图视图块的第一个视图。
        /// </summary>
        /// <param name="asset">提供反射视图块的渲染管线资源。</param>
        /// <param name="probe">正在渲染的探针（可为 <c>null</c>）。</param>
        /// <returns>选中的反射渲染图；不存在时返回 <c>null</c>。</returns>
        public static RenderGraphAsset SelectReflectionRenderGraph(
            HNRenderPipelineAsset asset,
            ReflectionProbe probe)
        {
            if (asset == null || asset.reflectionRenderGraphViewBlock == null)
            {
                return null;
            }

            int index = 0;
            if (probe != null)
            {
                HNAdditionalReflectionProbeData data = probe.GetHNAdditionalReflectionProbeData();
                if (data != null)
                {
                    index = data.RenderGraphViewIndex;
                }
            }

            return asset.reflectionRenderGraphViewBlock.GetRenderGraphObject(index)
                ?? asset.reflectionRenderGraphViewBlock.GetRenderGraphObject();
        }
    }
}
