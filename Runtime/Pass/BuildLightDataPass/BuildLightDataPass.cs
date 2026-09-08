// <copyright file="BuildLightDataPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
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
    /// Builds a GPU-resident light data buffer from the visible light list each frame.
    /// Uses a Burst-compiled <see cref="BuildLightDataJob"/> to convert
    /// <see cref="VisibleLight"/> entries into packed <see cref="LightData"/> structs,
    /// then uploads them to a compute buffer for downstream compute-shader passes.
    /// </summary>
    /// <remarks>
    /// <para><b>New Pass system</b> (ADR-002, ADR-011):
    /// Inherits from <see cref="Pass"/> instead of the legacy <see cref="PassBase"/>.
    /// Uses a name-based <see cref="ComputeBufferSlot"/> output for downstream
    /// connections instead of index-based slot registration.
    /// </para>
    /// <para>
    /// The buffer capacity is bounded by
    /// <see cref="HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN"/> +
    /// <see cref="HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN"/>.
    /// The compute shaders used by downstream passes (e.g. cluster culling) are
    /// accessed via <see cref="CameraContext.RuntimeResources"/>.
    /// </para>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class BuildLightDataPass : Pass
    {
        /// <summary>
        /// The constant pass name string used for registration and identification.
        /// Matches the legacy <see cref="BuildLightDataPass.PassName"/>.
        /// </summary>
        public const string PassNameConst = "Build Light Data";

        // ── Slots ──

        /// <summary>
        /// Gets the output compute buffer slot that holds the created light data
        /// buffer handle. Downstream passes connect their light data input slots
        /// to this output to receive the populated light data buffer.
        /// </summary>
        public ComputeBufferSlot LightDatasBufferSlot { get; private set; }

        // ── Per-frame state ──

        private CameraContext? m_Context;
        private NativeArray<VisibleLight> m_VisibleLights;
        private int m_LightCount;
        private int m_MaxLightCount;

        private BuildLightDataJob m_Job;
        private NativeArray<LightData> m_LightDatas;

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildLightDataPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public BuildLightDataPass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildLightDataPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public BuildLightDataPass(string passName)
            : base(passName)
        {
        }

        /// <inheritdoc />
        public override void CopyFrom(Pass source)
        {
            // No serialized parameters on this pass.
        }

        // ── Lifecycle ──

        /// <inheritdoc />
        public override void SetupSlots()
        {
            LightDatasBufferSlot = new ComputeBufferSlot("lightDatasBuffer", SlotDirection.Output);
            RegisterSlot(LightDatasBufferSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Captures the camera-specific rendering context to access per-frame
        /// visible light data from <see cref="CameraContext.VisibleLights"/>.
        /// The maximum light count is derived from the pipeline asset constants
        /// at initialization time.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            m_Context = context;
            m_VisibleLights = context.VisibleLights;
            m_MaxLightCount = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                            + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;
            m_LightCount = math.min(m_VisibleLights.Length, m_MaxLightCount);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Creates a transient compute buffer sized for the maximum light count,
        /// schedules a parallel <see cref="BuildLightDataJob"/> to pack visible
        /// lights into <see cref="LightData"/> structs, and records a render
        /// function that uploads the completed job output to the GPU buffer.
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRenderPass<BuildLightDataPassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                ComputeBufferHandle lightDatasBuffer = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        m_MaxLightCount,
                        UnsafeUtility.SizeOf<LightData>())
                    { name = "Light Datas Buffer" });

                passData.lightDatasBuffer = builder.WriteComputeBuffer(lightDatasBuffer);

                // Publish the real render graph handle so downstream
                // passes can read it via ReadHandle().
                LightDatasBufferSlot.SetHandle(lightDatasBuffer);

                m_LightDatas = new NativeArray<LightData>(m_LightCount, Allocator.TempJob);

                m_Job = new BuildLightDataJob
                {
                    visibleLights = m_VisibleLights,
                    lightDatas = m_LightDatas,
                };

                var jobHandle = m_Job.ScheduleParallel(m_LightCount, 1, new JobHandle());

                // Store per-frame values on the pooled pass data so the render
                // function closure only captures `this` (zero allocation).
                passData.jobHandle = jobHandle;
                passData.lightDatas = m_LightDatas;

                builder.SetRenderFunc(
                    (BuildLightDataPassData data, RenderGraphContext ctx) =>
                    {
                        if(!IsEnabled)
                        {
                            return;
                        }
                        
                        data.jobHandle.Complete();
                        ctx.cmd.SetBufferData(data.lightDatasBuffer, data.lightDatas);
                        data.lightDatas.Dispose();
                    });
            }
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // No disposable resources held beyond per-frame transient allocations
            // that are disposed inside the render function.
        }

        // ── Pass data ──

        /// <summary>
        /// Render graph pass data container for <see cref="BuildLightDataPass"/>.
        /// Holds the compute buffer handle populated by
        /// <c>builder.WriteComputeBuffer</c>.
        /// </summary>
        private class BuildLightDataPassData
        {
            /// <summary>
            /// The light data compute buffer handle.
            /// Populated by <c>builder.WriteComputeBuffer</c> during
            /// <see cref="Record"/>.
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// The job handle to complete before uploading the packed light data.
            /// </summary>
            public JobHandle jobHandle;

            /// <summary>
            /// The packed light data array uploaded to the GPU and disposed inside
            /// the render function.
            /// </summary>
            public NativeArray<LightData> lightDatas;
        }

        // ── Property IDs ──

        /// <summary>
        /// Shader property identifiers used by this pass and its consumers.
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// Global shader property ID for the light data structured buffer.
            /// Value: <c>_LightDatasBuffer</c>.
            /// </summary>
            public static readonly int LightDatasBuffer = Shader.PropertyToID("_LightDatasBuffer");
        }
    }
}
