// <copyright file="RealtimeProbeRenderer.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 在所有主相机之前渲染实时反射探针。从相机剔除结果收集可见的实时探针，
    /// 依据每个探针的 <see cref="ReflectionProbe.timeSlicingMode"/> 与
    /// <see cref="ReflectionProbe.refreshMode"/> 决定本帧渲染哪些 cubemap 面，
    /// 并通过 <see cref="HNRenderPipelineAsset.DefaultReflectionRenderGraph"/>
    /// 走常规逐相机管线渲染每个面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 面渲染使用 <see cref="ReflectionProbeCameraPool"/> 持有的池化相机；
    /// 池同时记录本帧已渲染的探针面，使对多个相机可见的探针只渲染一次。
    /// </para>
    /// <para>
    /// 反射探针自身不在探针 pass 内渲染 —— Reflection 渲染图模板
    /// 不包含 cluster-culling 探针 pass。
    /// </para>
    /// </remarks>
    public sealed class ReflectionProbeRenderer : IDisposable
    {
        /// <summary>
        /// 用于面渲染与逐帧去重的相机池。
        /// </summary>
        private readonly ReflectionProbeCameraPool pool;

        /// <summary>
        /// 本帧可见的实时探针，按探针实例 id 为键。由 <see cref="BeginFrame"/> 清除。
        /// </summary>
        private readonly Dictionary<int, ReflectionProbe> requests = new();

        /// <summary>
        /// 每个探针在
        /// <see cref="ReflectionProbeTimeSlicingMode.IndividualFaces"/> 下的面进度。
        /// </summary>
        private readonly Dictionary<int, int> faceProgress = new();

        /// <summary>
        /// 已针对 <see cref="ReflectionProbeRefreshMode.OnAwake"/> 初始化过的探针。
        /// </summary>
        private readonly HashSet<int> initializedProbes = new();

        /// <summary>
        /// 初始化 <see cref="ReflectionProbeRenderer"/> 的新实例。
        /// </summary>
        /// <param name="pool">用于面渲染的相机池。</param>
        public ReflectionProbeRenderer(ReflectionProbeCameraPool pool)
        {
            this.pool = pool;
        }

        /// <summary>
        /// 获取本帧待渲染的已收集实时探针数量。
        /// </summary>
        public int PendingProbeCount => requests.Count;

        /// <summary>
        /// 开始新帧：清除上一帧的请求与相机池的已渲染面集合。
        /// </summary>
        public void BeginFrame()
        {
            pool.BeginFrame();
            requests.Clear();
        }

        /// <summary>
        /// 从某相机的可见反射探针剔除结果中收集反射探针。
        /// 重复探针（对多个相机可见）只收集一次。
        /// </summary>
        /// <param name="visibleProbes">来自某相机剔除结果的可见反射探针。</param>
        public void CollectReflectionProbes(NativeArray<VisibleReflectionProbe> visibleProbes)
        {
            for (int i = 0; i < visibleProbes.Length; i++)
            {
                CollectRealtimeProbe(
                    ReflectionProbeRenderUtils.GetProbeInstanceId(visibleProbes[i]));
            }
        }

        /// <summary>
        /// 按实例 id 收集单个实时探针。无效 id、未知对象或非实时探针为无操作。
        /// </summary>
        /// <param name="probeInstanceId">反射探针实例 id。</param>
        public void CollectRealtimeProbe(int probeInstanceId)
        {
            if (probeInstanceId == 0 || requests.ContainsKey(probeInstanceId))
            {
                return;
            }

            var probe = UnityEngine.Resources.InstanceIDToObject(probeInstanceId) as ReflectionProbe;
            if (probe == null || !ReflectionProbeRenderUtils.IsRealtimeProbe(probe))
            {
                return;
            }

            requests.Add(probeInstanceId, probe);
        }

        /// <summary>
        /// 渲染所有已收集的反射探针。在主相机渲染前调用，使探针面先执行。
        /// 每个 cubemap 面在独立的 <c>RecordAndExecute</c> 块中录制并执行：
        /// <c>SetupCameraProperties</c> 设置的相机矩阵仅在块执行时生效，
        /// 因此每个面必须在配置完其相机后立即执行 —— 否则后续相机的矩阵
        /// 会泄漏到本面的 pass 中。
        /// </summary>
        /// <param name="context">本帧的脚本化渲染上下文。</param>
        /// <param name="renderGraph">用于录制探针 pass 的渲染图。</param>
        /// <param name="parameters">本帧的渲染图参数。</param>
        /// <param name="asset">提供反射渲染图与运行时资源的管线资源。</param>
        public void RenderProbes(
            ScriptableRenderContext context,
            RenderGraph renderGraph,
            in RenderGraphParameters parameters,
            HNRenderPipelineAsset asset)
        {
            if (requests.Count == 0 || asset == null || asset.reflectionRenderGraphViewBlock == null)
            {
                return;
            }
            
            foreach (KeyValuePair<int, ReflectionProbe> request in requests)
            {
                RenderProbe(context, renderGraph, parameters, asset, request.Value);
            }
        }

        /// <summary>
        /// 返回给定探针本帧是否应渲染，遵循其
        /// <see cref="ReflectionProbe.refreshMode"/>。
        /// </summary>
        /// <param name="probe">要测试的反射探针。</param>
        /// <returns>
        /// <see cref="ReflectionProbeRefreshMode.EveryFrame"/> 时返回 <c>true</c>；
        /// <see cref="ReflectionProbeRefreshMode.OnAwake"/> 仅在初始化完成前返回
        /// <c>true</c>；<see cref="ReflectionProbeRefreshMode.ViaScripting"/> 返回
        /// <c>false</c>。
        /// </returns>
        public bool ShouldRenderThisFrame(ReflectionProbe probe)
        {
            if (probe == null)
            {
                return false;
            }

            switch (probe.refreshMode)
            {
                case ReflectionProbeRefreshMode.OnAwake:
                    return !initializedProbes.Contains(probe.GetInstanceID());

                case ReflectionProbeRefreshMode.ViaScripting:
                    return false;

                case ReflectionProbeRefreshMode.EveryFrame:
                default:
                    return true;
            }
        }

        /// <summary>
        /// 将探针标记为已初始化，使
        /// <see cref="ReflectionProbeRefreshMode.OnAwake"/> 探针停止渲染。
        /// </summary>
        /// <param name="probe">已完成初始渲染的探针。</param>
        public void MarkInitialized(ReflectionProbe probe)
        {
            if (probe == null)
            {
                return;
            }

            initializedProbes.Add(probe.GetInstanceID());
        }

        /// <summary>
        /// 返回给定探针是否已初始化。
        /// </summary>
        /// <param name="probe">要测试的反射探针。</param>
        /// <returns>探针完成初始渲染后返回 <c>true</c>。</returns>
        public bool IsInitialized(ReflectionProbe probe)
        {
            return probe != null && initializedProbes.Contains(probe.GetInstanceID());
        }

        /// <summary>
        /// 结束渲染器与其相机池的本帧处理。
        /// </summary>
        public void EndFrame()
        {
            pool.EndFrame();
        }

        /// <summary>
        /// 释放渲染器及其相机池。**不**销毁探针的实时 cubemap：该
        /// <see cref="RenderTexture"/> 归 <see cref="ReflectionProbe"/> 所有，
        /// 由 Unity 在探针销毁时统一清理；在此销毁会导致探针持有悬空引用，
        /// 进而在场景恢复 / 退出 Play 时于 ReflectionProbe::ClearRenderTextures 崩溃。
        /// </summary>
        public void Dispose()
        {
            pool.Dispose();
            requests.Clear();
            faceProgress.Clear();
            initializedProbes.Clear();
        }

        // ── 渲染 ──

        private void RenderProbe(
            ScriptableRenderContext context,
            RenderGraph renderGraph,
            in RenderGraphParameters parameters,
            HNRenderPipelineAsset asset,
            ReflectionProbe probe)
        {
            if (!ShouldRenderThisFrame(probe))
            {
                return;
            }

            int probeId = probe.GetInstanceID();
            int[] faces = GetFacesForProbe(probe, probeId);

            foreach (int face in faces)
            {
                if (pool.IsFaceRendered(probeId, face))
                {
                    continue;
                }

                RenderFace(context, renderGraph, parameters, asset, probe, face);
                pool.MarkFaceRendered(probeId, face);
            }

            if (probe.refreshMode == ReflectionProbeRefreshMode.OnAwake)
            {
                MarkInitialized(probe);
            }
            else if (faces.Length > 0 &&
                     probe.timeSlicingMode == ReflectionProbeTimeSlicingMode.IndividualFaces)
            {
                int progress = GetFaceProgress(probeId);
                faceProgress[probeId] = ReflectionProbeRenderUtils.AdvanceIndividualFace(progress);
            }
        }

        private int[] GetFacesForProbe(ReflectionProbe probe, int probeId)
        {
            if (probe.refreshMode == ReflectionProbeRefreshMode.OnAwake)
            {
                // OnAwake 一次性渲染所有面。
                return ReflectionProbeRenderUtils.AllFaces;
            }

            return ReflectionProbeRenderUtils.GetFacesToRender(
                probe.timeSlicingMode,
                probeId,
                Time.frameCount,
                GetFaceProgress(probeId));
        }

        private int GetFaceProgress(int probeId)
        {
            return faceProgress.TryGetValue(probeId, out int progress) ? progress : 0;
        }

        private void RenderFace(
            ScriptableRenderContext context,
            RenderGraph renderGraph,
            in RenderGraphParameters parameters,
            HNRenderPipelineAsset asset,
            ReflectionProbe probe,
            int face)
        {
            Camera camera = pool.GetCamera();
            RenderTexture target = GetProbeTarget(probe);
            ConfigureCamera(camera, probe, face, target);

            int probeId = probe.GetInstanceID();
            var customTargetHandle = pool.GetOrCreateFaceHandle(probeId, face, target);
            var cameraContext = new CameraContext(camera, context)
            {
                RuntimeResources = asset.runtimeResources,
                TargetFace = (CubemapFace)face,
                TargetDepthSlice = 0,
                Flip = true,
                CustomTargetRTHandle = customTargetHandle,
            };

            bool gotParams = camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParams);
            if (gotParams)
            {
                cameraContext.CullingResults = context.Cull(ref cullingParams);
                cameraContext.HasCullingResults = true;
                cameraContext.VisibleLights = new NativeArray<VisibleLight>(
                    cameraContext.CullingResults.visibleLights, Allocator.TempJob);
                // VisibleReflectionProbes 有意不填充：Reflection 渲染图模板
                // 没有反射探针消费方。
            }

            // 每个面在独立的 RecordAndExecute 块中录制并执行，使下面配置的
            // 相机矩阵在 pass 运行时是当前生效的矩阵。
            using (renderGraph.RecordAndExecute(parameters))
            {
                SetupCameraProperties(context, camera);

                // 将该面相机的全局着色器常量推入 ShaderVariablesGlobal cbuffer。
                // 渲染图命令在本相机块之后提交，因此各面的矩阵在面的绘制期间
                // 生效（仅调用 SetupCameraProperties 会让上一个相机的矩阵
                // 残留为全局状态）。
                var globalConstantBuffer = new GlobalConstantBuffer();
                GlobalConstantBufferUtility.FillFromCamera(
                    camera,
                    renderIntoTexture: true,
                    ref globalConstantBuffer);
                ConstantBuffer.PushGlobal(
                    parameters.commandBuffer,
                    globalConstantBuffer,
                    GlobalPropertyIDs.ShaderVariablesGlobal);

                CameraRenderer renderer =
                    camera.GetHNRPAdditionalCameraData().GetOrCreateRenderer();
                renderer.Build(ReflectionProbeRenderUtils.SelectReflectionRenderGraph(asset, probe));
                renderer.Render(renderGraph, cameraContext);
            }

            cameraContext.Dispose();
        }

        private static void SetupCameraProperties(ScriptableRenderContext context, Camera camera)
        {
            var cmd = CommandBufferPool.Get("CameraSetup");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);

            context.SetupCameraProperties(camera);
        }

        private static void ConfigureCamera(
            Camera camera,
            ReflectionProbe probe,
            int face,
            RenderTexture target)
        {
            camera.transform.position = probe.transform.position;
            camera.transform.rotation = ReflectionProbeRenderUtils.GetFaceRotation(face);
            camera.cameraType = CameraType.Reflection;
            camera.targetTexture = target;
            camera.nearClipPlane = probe.nearClipPlane;
            camera.farClipPlane = probe.farClipPlane;
            camera.cullingMask = probe.cullingMask;
            camera.clearFlags = (CameraClearFlags)probe.clearFlags;
            camera.backgroundColor = probe.backgroundColor;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.ResetProjectionMatrix();
        }

        private static RenderTexture GetProbeTarget(ReflectionProbe probe)
        {
            var format = probe.hdr ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
            var existing = probe.realtimeTexture;

            // 现有 RT 尺寸/格式/维度均匹配才复用；否则说明 probe 的
            // resolution/hdr 已变化，需按新参数重建 cubemap。
            // 旧 RT 不在此销毁：它归 ReflectionProbe 所有，由 Unity 在探针销毁
            // 时清理；在此 DestroyImmediate 会让探针持有悬空引用，导致
            // ReflectionProbe::ClearRenderTextures 崩溃。
            if (existing != null &&
                existing.dimension == TextureDimension.Cube &&
                existing.width == probe.resolution &&
                existing.height == probe.resolution &&
                existing.format == format)
            {
                return existing;
            }

            var descriptor = new RenderTextureDescriptor(
                probe.resolution,
                probe.resolution,
                format,
                0)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
            };

            var rt = new RenderTexture(descriptor)
            {
                name = "RealtimeProbeRT_" + probe.name,
            };

            probe.realtimeTexture = rt;
            return rt;
        }
    }
}
