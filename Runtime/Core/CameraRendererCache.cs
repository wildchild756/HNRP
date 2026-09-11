// <copyright file="CameraRendererCache.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// 按相机实例 id 缓存运行时 <see cref="CameraRenderer"/>，使 pass 实例及其
    /// 持有资源（如阴影 atlas、驻留分配表）跨帧存活。相机销毁或缓存释放时，
    /// 对应渲染器的 pass 资源被清理。
    /// </summary>
    /// <remarks>
    /// 主相机与反射探针面相机（池化复用）共用同一份缓存：同一相机每帧取回
    /// 同一个渲染器，其 pass 列表被复用而非每帧重建。
    /// </remarks>
    public sealed class CameraRendererCache
    {
        /// <summary>
        /// 键为相机实例 id 的渲染器缓存。
        /// </summary>
        private readonly Dictionary<int, CameraRenderer> renderers = new();

        /// <summary>
        /// 取指定相机缓存的渲染器；不存在时用给定上下文创建，并更新其上下文。
        /// </summary>
        /// <param name="camera">目标相机。</param>
        /// <param name="context">当前帧相机上下文。</param>
        /// <returns>该相机的渲染器实例。</returns>
        public CameraRenderer GetOrCreate(Camera camera, CameraContext context)
        {
            int id = camera.GetInstanceID();
            if (!renderers.TryGetValue(id, out CameraRenderer renderer))
            {
                renderer = new CameraRenderer(context);
                renderers[id] = renderer;
            }

            renderer.Context = context;
            return renderer;
        }

        /// <summary>
        /// 移除并释放已销毁相机对应的渲染器。每帧调用以回收相机销毁后残留的资源。
        /// </summary>
        public void RemoveDestroyed()
        {
            List<int> stale = null;
            foreach (KeyValuePair<int, CameraRenderer> pair in renderers)
            {
                // Unity 对象的空引用：相机被销毁后 `Camera == null` 为 true。
                if (pair.Value.Context?.Camera == null)
                {
                    (stale ??= new List<int>()).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (int id in stale)
            {
                renderers[id].Dispose();
                renderers.Remove(id);
            }
        }

        /// <summary>
        /// 释放全部缓存的渲染器并清空缓存。
        /// </summary>
        public void Dispose()
        {
            foreach (CameraRenderer renderer in renderers.Values)
            {
                renderer.Dispose();
            }

            renderers.Clear();
        }
    }
}
