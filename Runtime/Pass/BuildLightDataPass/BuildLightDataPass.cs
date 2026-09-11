// <copyright file="BuildLightDataPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 构建光照数据：把可见光打包进计算缓冲，供前向/簇剔除等 pass 消费。
    /// </summary>
    [Pass(PassNameConst)]
    public sealed class BuildLightDataPass : Pass
    {
        /// <summary>
        /// 用于注册与识别的常量 pass 名。与旧 <see cref="BuildLightDataPass.PassName"/> 一致。
        /// </summary>
        public const string PassNameConst = "Build Light Data";

        // ── Slot ──

        /// <summary>光照数据计算缓冲输出 slot。</summary>
        public ComputeBufferSlot LightDatasBufferSlot { get; private set; }

        // ── 每帧状态 ──

        private CameraContext cameraContext;
        private NativeArray<VisibleLight> visibleLights;
        private int lightCount;
        private int maxLightCount; 

        private BuildLightDataJob job;
        private NativeArray<uint> renderingLayerMasks;
        private NativeArray<LightData> lightDatas;

        // ── 构造函数 ──

        /// <summary>
        /// 初始化 <see cref="BuildLightDataPass"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 无参构造仅供 <see cref="RenderGraphAsset"/> 上参数缓存 Pass 的
        /// <c>[SerializeReference]</c> 反序列化使用；实例名随后由序列化数据填充。
        /// </remarks>
        public BuildLightDataPass()
            : base(string.Empty)
        {
        }

        /// <summary>
        /// 初始化 <see cref="BuildLightDataPass"/> 的新实例。
        /// </summary>
        /// <param name="passName">
        /// 本 pass 的实例名。必须非 null 且在渲染图内唯一。
        /// </param>
        public BuildLightDataPass(string passName)
            : base(passName)
        {
        }

        // ── 生命周期 ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            LightDatasBufferSlot = new ComputeBufferSlot("lightDatasBuffer", SlotDirection.Output);
            RegisterSlot(LightDatasBufferSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 捕获相机专属渲染上下文，以访问 <see cref="CameraContext.VisibleLights"/>
        /// 的每帧可见光数据。最大光源数在初始化期由管线资产常量推导。
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            cameraContext = context;
            visibleLights = context.VisibleLights;
            maxLightCount = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                            + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;
            lightCount = math.min(visibleLights.Length, maxLightCount);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 创建按最大光源数尺寸的瞬时计算缓冲，调度并行 <see cref="BuildLightDataJob"/>
        /// 把可见光打包进 <see cref="LightData"/> 结构体，并记录一个把完成的 job
        /// 输出上传到 GPU 缓冲的渲染函数。
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRenderPass<BuildLightDataPassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                var renderingLayerMaskList = new uint[visibleLights.Length];
                for(int i = 0; i < visibleLights.Length; i++)
                {
                    if(visibleLights[i].light.TryGetComponent<HNAdditionalLightData>(out var lightData))
                    {
                        renderingLayerMaskList[i] = lightData.RenderingLayerMask;
                    }
                    else
                    {
                        renderingLayerMaskList[i] = 0;
                    }
                }

                ComputeBufferHandle lightDatasBuffer = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        maxLightCount,
                        UnsafeUtility.SizeOf<LightData>())
                    { name = "Light Datas Buffer" });

                passData.lightDatasBuffer = builder.WriteComputeBuffer(lightDatasBuffer);

                // 发布真实渲染图句柄，使下游 pass 能经 ReadHandle() 读取。
                LightDatasBufferSlot.SetHandle(lightDatasBuffer);

                renderingLayerMasks = new NativeArray<uint>(renderingLayerMaskList, Allocator.TempJob);
                lightDatas = new NativeArray<LightData>(lightCount, Allocator.TempJob);

                job = new BuildLightDataJob
                {
                    visibleLights = visibleLights,
                    renderingLayerMasks = renderingLayerMasks,
                    lightDatas = lightDatas,
                };

                var jobHandle = job.ScheduleParallel(lightCount, 1, new JobHandle());

                // 把每帧值存到池化 pass 数据上，使渲染函数闭包只捕获 `this`（零分配）。
                passData.jobHandle = jobHandle;
                passData.lightDatas = lightDatas;
                passData.renderingLayerMasks = renderingLayerMasks;

                builder.SetRenderFunc(
                    (BuildLightDataPassData data, RenderGraphContext ctx) =>
                    {
                        data.jobHandle.Complete();

                        if (IsEnabled)
                        {
                            ctx.cmd.SetBufferData(data.lightDatasBuffer, data.lightDatas);
                        }

                        // 两个 TempJob 原生数组在 job 完成后必须释放，
                        // 否则超过 4 帧生命周期触发 JobTempAlloc 泄漏告警。
                        data.lightDatas.Dispose();
                        data.renderingLayerMasks.Dispose();
                    });
            }
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // 除渲染函数内释放的每帧瞬时分配外，不持有其他可释放资源。
        }

        // ── Pass data ──

        /// <summary>
        /// <see cref="BuildLightDataPass"/> 的渲染图 pass 数据容器。
        /// 持有由 <c>builder.WriteComputeBuffer</c> 填充的计算缓冲句柄。
        /// </summary>
        private class BuildLightDataPassData
        {
            /// <summary>
            /// 光照数据计算缓冲句柄。在 <see cref="Record"/> 中由
            /// <c>builder.WriteComputeBuffer</c> 填充。
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// 上传打包光照数据前需完成的 job 句柄。
            /// </summary>
            public JobHandle jobHandle;

            /// <summary>
            /// 上传到 GPU 并在渲染函数内释放的打包光照数据数组。
            /// </summary>
            public NativeArray<LightData> lightDatas;

            /// <summary>
            /// job 读取的渲染层掩码数组；job 完成后在渲染函数内释放。
            /// </summary>
            public NativeArray<uint> renderingLayerMasks;
        }

        // ── Property IDs ──

        /// <summary>
        /// 本 pass 与其消费方使用的 shader 属性标识。
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// 光照数据结构化缓冲的全局 shader 属性 ID。值：<c>_LightDatasBuffer</c>。
            /// </summary>
            public static readonly int LightDatasBuffer = Shader.PropertyToID("_LightDatasBuffer");
        }
    }
}
