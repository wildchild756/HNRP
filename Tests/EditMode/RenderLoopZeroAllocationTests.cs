// <copyright file="RenderLoopZeroAllocationTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Verifies that the pass render loop records zero managed allocations
    /// (0GC) when executed against a real <see cref="RenderGraph"/>.
    /// Focuses on the pass-owned resource model (ADR-017): a chain-head
    /// <see cref="DrawObjectPass"/> allocates its color / depth targets locally
    /// while its render function closure only captures <c>this</c>.
    /// </summary>
    public sealed class RenderLoopZeroAllocationTests
    {
        /// <summary>
        /// <see cref="DrawObjectPass.Record"/> with unconnected input slots
        /// (self-allocation path) must record with zero managed allocations.
        /// </summary>
        [Test]
        public void DrawObjectPass_Record_SelfAllocation_ZeroGc()
        {
            var renderGraph = new UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraph("GC");
            CommandBuffer cmd = CommandBufferPool.Get("GC");
            var parameters = new UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraphParameters
            {
                currentFrameIndex = 1,
                executionName = "GC",
                scriptableRenderContext = default,
                commandBuffer = cmd,
                rendererListCulling = false,
            };

            var go = new GameObject("ZeroGcCamera");
            var camera = go.AddComponent<Camera>();
            var context = new CameraContext(camera, default);
            var pass = new DrawObjectPass("opaque");
            pass.SetupSlots();
            pass.PreRecord(new RenderGraphAsset(), context);

            try
            {
                // Warm-up pass so lazy caches (scratch buffers, pools) are hot.
                using (renderGraph.RecordAndExecute(parameters))
                {
                    pass.ResetSlotHandles();
                    pass.Record(renderGraph);
                }

                renderGraph.EndFrame();

                var recorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory, "GC Alloc");
                using (renderGraph.RecordAndExecute(parameters))
                {
                    pass.ResetSlotHandles();
                    pass.Record(renderGraph);
                }

                renderGraph.EndFrame();
                recorder.Stop();

                Assert.That(recorder.LastValue, Is.EqualTo(0),
                    "DrawObjectPass.Record (self-allocation path) must not allocate managed memory.");
            }
            finally
            {
                CommandBufferPool.Release(cmd);
                renderGraph.Cleanup();
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// <see cref="ClusterCullingReflectionProbePass.Record"/> with no visible
        /// probes (atlas self-allocation + compute buffers) must record with zero
        /// managed allocations. The pass reuses pre-allocated scratch buffers and
        /// its render function closure only captures <c>this</c>.
        /// </summary>
        [Test]
        public void ClusterCullingReflectionProbePass_Record_ZeroGc()
        {
            var resources = ScriptableObject.CreateInstance<HNRenderPipelineRuntimeResources>();
            var renderGraph = new UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraph("GC");
            CommandBuffer cmd = CommandBufferPool.Get("GC");
            var parameters = new UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraphParameters
            {
                currentFrameIndex = 1,
                executionName = "GC",
                scriptableRenderContext = default,
                commandBuffer = cmd,
                rendererListCulling = false,
            };

            var go = new GameObject("ZeroGcCamera");
            var camera = go.AddComponent<Camera>();
            var context = new CameraContext(camera, default)
            {
                RuntimeResources = resources,
            };
            ComputeShader cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HNRP/Runtime/ShaderLibrary/ComputeShaders/ClusterCullingReflectionProbeCS.compute");
            Assume.That(cs, Is.Not.Null,
                "Cluster culling reflection probe compute shader must be loadable for this test.");
            resources.clusterCullingReflectionProbeCS = cs;

            var pass = new ClusterCullingReflectionProbePass("clusterProbe");
            pass.SetupSlots();
            pass.PreRecord(new RenderGraphAsset(), context);

            try
            {
                // Warm-up pass so lazy caches (scratch buffers, pools) are hot.
                using (renderGraph.RecordAndExecute(parameters))
                {
                    pass.ResetSlotHandles();
                    pass.Record(renderGraph);
                }

                renderGraph.EndFrame();

                var recorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory, "GC Alloc");
                using (renderGraph.RecordAndExecute(parameters))
                {
                    pass.ResetSlotHandles();
                    pass.Record(renderGraph);
                }

                renderGraph.EndFrame();
                recorder.Stop();

                Assert.That(recorder.LastValue, Is.EqualTo(0),
                    "ClusterCullingReflectionProbePass.Record must not allocate managed memory.");
            }
            finally
            {
                CommandBufferPool.Release(cmd);
                renderGraph.Cleanup();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(resources);
            }
        }
    }
}
