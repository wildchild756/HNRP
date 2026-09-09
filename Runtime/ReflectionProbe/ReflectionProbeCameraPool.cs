// <copyright file="RealtimeProbeCameraPool.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 用于渲染实时反射探针 cubemap 面的 <see cref="Camera"/> 实例池。
    /// 相机跨帧复用而非每帧重建；池记录本帧已渲染的探针面，避免重叠相机
    /// 重复渲染同一探针。同时管理探针 cubemap 面的 <see cref="RTHandle"/>，
    /// 避免每帧重新分配。
    /// </summary>
    public sealed class ReflectionProbeCameraPool : IDisposable
    {
        private Camera camera;

        /// <summary>
        /// 本帧已渲染的探针面。键为 <c>probeInstanceId * 6 + face</c>。
        /// </summary>
        private readonly HashSet<int> renderedFaces = new();

        /// <summary>
        /// 探针 cubemap 面的缓存 RTHandle，键为 <c>probeInstanceId * 6 + face</c>。
        /// 句柄跨帧存在，仅在 <see cref="Dispose"/> 时释放。
        /// </summary>
        private readonly Dictionary<int, RTHandle> probeFaceHandles = new();

        /// <summary>
        /// 每个面句柄对应的 cubemap 实例 id，键为 <c>probeInstanceId * 6 + face</c>。
        /// 用于检测探针实时 cubemap 是否被重建（其实例 id 会变化），
        /// 以便丢弃过期 <see cref="RTHandle"/> 并针对新 cubemap 重建。
        /// </summary>
        private readonly Dictionary<int, int> probeFaceCubemapIds = new();

        /// <summary>
        /// 从池中获取相机；池为空时创建新相机。
        /// 使用完毕后调用方须经 <see cref="ReturnCamera"/> 归还。
        /// </summary>
        /// <returns>用于渲染探针面的相机。</returns>
        public Camera GetCamera()
        {
            if (camera == null)
            {
                camera = CreateCamera();
            }

            return camera;
        }

        /// <summary>
        /// 返回给定探针面本帧是否已渲染。
        /// </summary>
        /// <param name="probeInstanceId">反射探针实例 id。</param>
        /// <param name="face">cubemap 面索引（<c>0..5</c>）。</param>
        /// <returns>该面本帧已渲染时返回 <c>true</c>。</returns>
        public bool IsFaceRendered(int probeInstanceId, int face)
        {
            return renderedFaces.Contains(Encode(probeInstanceId, face));
        }

        /// <summary>
        /// 将给定探针面标记为本帧已渲染，使后续请求跳过它。
        /// </summary>
        /// <param name="probeInstanceId">反射探针实例 id。</param>
        /// <param name="face">cubemap 面索引（<c>0..5</c>）。</param>
        public void MarkFaceRendered(int probeInstanceId, int face)
        {
            renderedFaces.Add(Encode(probeInstanceId, face));
        }

        /// <summary>
        /// 获取或创建某探针 cubemap 面的缓存 <see cref="RTHandle"/>。
        /// 句柄包装指向该 cubemap 指定面的 <see cref="RenderTargetIdentifier"/>。
        /// 句柄跨帧复用，在 <see cref="Dispose"/> 时释放。
        /// </summary>
        /// <param name="probeInstanceId">探针实例 id。</param>
        /// <param name="face">cubemap 面索引（0..5）。</param>
        /// <param name="cubemap">cubemap 渲染纹理。</param>
        /// <returns>该探针面的缓存 RTHandle。</returns>
        public RTHandle GetOrCreateFaceHandle(int probeInstanceId, int face, RenderTexture cubemap)
        {
            int key = Encode(probeInstanceId, face);
            if (probeFaceHandles.TryGetValue(key, out RTHandle existing))
            {
                // 缓存句柄仅在 cubemap 标识不变时有效。Unity 在探针参数
                // 变化时会重建 probe.realtimeTexture，因此过期的句柄
                // （持有已销毁 RenderTexture 的实例 id）必须重建。
                if (probeFaceCubemapIds.TryGetValue(key, out int cachedInstanceId) &&
                    cachedInstanceId == cubemap.GetInstanceID())
                {
                    return existing;
                }

                existing?.Release();
                probeFaceHandles.Remove(key);
                probeFaceCubemapIds.Remove(key);
            }

            var targetId = new RenderTargetIdentifier(cubemap, 0, (CubemapFace)face, 0);
            var handle = RTHandles.Alloc(targetId, "RealtimeProbeFace" + key);
            probeFaceHandles[key] = handle;
            probeFaceCubemapIds[key] = cubemap.GetInstanceID();
            return handle;
        }

        /// <summary>
        /// 开始新帧：清除上一帧的已渲染面集合与已渲染探针纹理。
        /// </summary>
        public void BeginFrame()
        {
            renderedFaces.Clear();
        }

        /// <summary>
        /// 结束本帧。保留作为扩展点；相机由调用方归还，
        /// 已渲染集合由 <see cref="BeginFrame"/> 清除。
        /// </summary>
        public void EndFrame()
        {
        }

        /// <summary>
        /// 销毁所有池化相机、释放全部缓存 RTHandle 并清空所有状态。
        /// </summary>
        public void Dispose()
        {
            if (camera != null)
            {
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }

            foreach (RTHandle handle in probeFaceHandles.Values)
            {
                handle?.Release();
            }

            probeFaceHandles.Clear();
            probeFaceCubemapIds.Clear();
            renderedFaces.Clear();
        }

        private static int Encode(int probeInstanceId, int face)
        {
            return probeInstanceId * 6 + face;
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("ReflectionProbeCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = go.AddComponent<Camera>();
            camera.cameraType = CameraType.Reflection;
            camera.enabled = false;
            return camera;
        }
    }
}
