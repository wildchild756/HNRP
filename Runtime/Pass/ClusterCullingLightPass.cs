// <copyright file="ClusterCullingLightPass.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// Performs cluster-based light culling using a compute shader.
    /// Reads the light data buffer produced by <see cref="BuildLightDataPass"/>,
    /// dispatches the cluster culling compute shader, and outputs a light mask
    /// buffer consumed by forward rendering passes.
    /// </summary>
    /// <remarks>
    /// <para><b>New Pass system</b> (ADR-002, ADR-011):
    /// Inherits from <see cref="Pass"/> instead of the legacy <see cref="PassBase"/>.
    /// The compute shader is accessed via <see cref="CameraContext.RuntimeResources"/>
    /// instead of being loaded from the AssetDatabase at creation time.
    /// Uses name-based <see cref="ComputeBufferSlot"/> for input/output connections.
    /// </para>
    /// <para>
    /// <b>Inputs:</b>
    /// <list type="bullet">
    ///   <item><b>lightDatasBuffer</b> — the light data compute buffer from
    ///   <see cref="BuildLightDataPass"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Outputs:</b>
    /// <list type="bullet">
    ///   <item><b>clusterCullingLightMaskBuffer</b> — the cluster culling light
    ///   mask buffer for forward rendering passes.</item>
    /// </list>
    /// </para>
    /// </remarks>
    [Pass(PassNameConst)]
    public sealed class ClusterCullingLightPass : Pass
    {
        /// <summary>
        /// The constant pass name string used for registration and identification.
        /// </summary>
        public const string PassNameConst = "Cluster Culling Light";

        // ── Slots ──

        /// <summary>
        /// Gets the input light data buffer slot.
        /// Connected to the output of <see cref="BuildLightDataPass"/>.
        /// </summary>
        public ComputeBufferSlot? LightDatasBufferSlot { get; private set; }

        /// <summary>
        /// Gets the output cluster culling light mask buffer slot.
        /// Connected to the light mask input of forward rendering passes.
        /// </summary>
        public ComputeBufferSlot? ClusterCullingLightMaskBufferSlot { get; private set; }

        // ── Camera context ──

        private CameraContext? m_CameraContext;

        // ── Constructor ──

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterCullingLightPass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization) and preset templates.
        /// </summary>
        public ClusterCullingLightPass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterCullingLightPass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The instance name of this pass. Must be non-null and unique within the render graph.
        /// </param>
        public ClusterCullingLightPass(string passName)
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
            LightDatasBufferSlot = new ComputeBufferSlot(
                "lightDatasBuffer", SlotDirection.Input);
            RegisterSlot(LightDatasBufferSlot);
            ClusterCullingLightMaskBufferSlot = new ComputeBufferSlot(
                "clusterCullingLightMaskBuffer", SlotDirection.Output);
            RegisterSlot(ClusterCullingLightMaskBufferSlot);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Stores the camera context so the compute shader and camera data can be
        /// accessed during <see cref="Record"/>. The compute shader is resolved from
        /// <see cref="CameraContext.RuntimeResources"/>.
        /// </remarks>
        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            m_CameraContext = context;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reads the light data buffer from the connected input slot, creates the
        /// output cluster culling light mask buffer, configures and dispatches the
        /// cluster culling compute shader, and publishes the output handle.
        ///
        /// The compute shader and camera matrices come from the camera context
        /// set during <see cref="PreRecord"/>.
        /// </remarks>
        public override void Record(RenderGraph renderGraph)
        {
            if (ClusterCullingLightMaskBufferSlot == null
                || LightDatasBufferSlot == null)
            {
                IsEnabled &= false;
            }

            if (m_CameraContext == null)
            {
                IsEnabled &= false;
            }

            ComputeShader clusterCullingLightCS =
                m_CameraContext.RuntimeResources?.clusterCullingLightCS;
            if (clusterCullingLightCS == null)
            {
                Debug.LogError(
                    "Cluster Culling Light Compute Shader is null. " +
                    "Ensure it is assigned in HNRenderPipelineRuntimeResources.");
                IsEnabled &= false;
            }

            Camera camera = m_CameraContext.Camera;
            if (camera == null)
            {
                IsEnabled &= false;
            }

            using (var builder = renderGraph.AddRenderPass<ClusterCullingLightPassData>(
                PassName, out var passData))
            {
                builder.AllowPassCulling(false);

                // ── Input: light data buffer ──

                if (LightDatasBufferSlot?.IsConnected == true)
                {
                    passData.lightDatasBuffer = builder.ReadComputeBuffer(
                        LightDatasBufferSlot.ReadHandle());
                }

                // ── Output: cluster culling light mask buffer ──

                ComputeBufferHandle lightMaskBuffer = renderGraph.CreateComputeBuffer(
                    new ComputeBufferDesc(
                        MAX_CLUSTER_MASK_WORDS,
                        sizeof(uint))
                    { name = "Cluster Culling Light Mask Buffer" });

                passData.clusterCullingLightMaskBuffer = builder.WriteComputeBuffer(lightMaskBuffer);

                ClusterCullingLightMaskBufferSlot.SetHandle(lightMaskBuffer);

                // ── Prepare per-frame data ──

                int maxLightOnScreen =
                    HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                    + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;
                int catchedLightCount = Mathf.Min(
                    m_CameraContext.VisibleLights.Length, maxLightOnScreen);

                int directionalLightCount = 0;
                int localLightCount = 0;
                for (int i = 0; i < catchedLightCount; i++)
                {
                    var light = m_CameraContext.VisibleLights[i];
                    if (light.lightType == LightType.Directional)
                    {
                        directionalLightCount++;
                    }

                    if (light.lightType == LightType.Point
                        || light.lightType == LightType.Spot)
                    {
                        localLightCount++;
                    }
                }

                if (directionalLightCount > 0)
                {
                    directionalLightCount -= 1;
                }

                int2 screenResolution =
                    math.int2(camera.pixelWidth, camera.pixelHeight);
                int3 clusterSize = GetClusterSize(screenResolution);
                int clusterCount =
                    clusterSize.x * clusterSize.y * clusterSize.z;
                float2 clusterZScaleOffset = GetClusterZScaleOffset(
                    clusterSize,
                    camera.orthographic,
                    camera.nearClipPlane,
                    camera.farClipPlane);

                // Items per cluster = total visible lights on screen
                int itemsPerCluster = maxLightOnScreen;
                int wordsPerCluster =
                    (itemsPerCluster + 31) / 32 + 1 /* 1 for header */;

                // ── Configure pass data ──

                passData.clusterCullingLightCS = clusterCullingLightCS;
                passData.clusterCullingLightKernel =
                    clusterCullingLightCS.FindKernel(
                        CLUSTER_CULLING_CS_KERNEL_NAME);

                passData.clusterCullingLightParams.clusterSize =
                    new Vector2(clusterSize.x, clusterSize.y);
                passData.clusterCullingLightParams.clusterZScaleOffset =
                    new Vector2(clusterZScaleOffset.x, clusterZScaleOffset.y);
                passData.clusterCullingLightParams.wordsPerCluster =
                    wordsPerCluster;
                passData.clusterCullingLightParams.directionalLightCount =
                    directionalLightCount;
                passData.clusterCullingLightParams.localLightCount =
                    localLightCount;
                passData.clusterCullingLightParams.unused = 0;

                // Camera matrices
                Matrix4x4 clipToView = camera.projectionMatrix;
                Matrix4x4 viewToClip = camera.projectionMatrix.inverse;
                Matrix4x4 clipToWorld =
                    (camera.worldToCameraMatrix * camera.projectionMatrix)
                    .inverse;

                // ── Store per-frame values on the pooled pass data so the
                // render function closure only captures `this` (zero allocation) ──

                passData.clusterSize = clusterSize;
                passData.clusterZScaleOffset = clusterZScaleOffset;
                passData.wordsPerCluster = wordsPerCluster;
                passData.catchedLightCount = catchedLightCount;
                passData.cameraOrthographic = camera.orthographic;
                passData.clipToView = clipToView;
                passData.viewToClip = viewToClip;
                passData.clipToWorld = clipToWorld;

                // ── Render function ──

                builder.SetRenderFunc(
                    (ClusterCullingLightPassData data, RenderGraphContext ctx) =>
                    {
                        if(!IsEnabled)
                        {
                            return;
                        }

                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            PropertyIDs.clusterCullingLightMaskBuffer,
                            data.clusterCullingLightMaskBuffer);
                        ctx.cmd.SetComputeBufferParam(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            BuildLightDataPass.PropertyIDs.LightDatasBuffer,
                            data.lightDatasBuffer);

                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingParams0,
                            new Vector4(
                                data.clusterZScaleOffset.x,
                                data.clusterZScaleOffset.y,
                                data.wordsPerCluster,
                                data.cameraOrthographic ? 1.0f : 0.0f));
                        ctx.cmd.SetComputeVectorParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingParams1,
                            new Vector4(
                                data.clusterSize.x,
                                data.clusterSize.y,
                                data.clusterSize.z,
                                data.catchedLightCount));

                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingClipToViewMatrix,
                            data.clipToView);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingViewToClipMatrix,
                            data.viewToClip);
                        ctx.cmd.SetComputeMatrixParam(
                            data.clusterCullingLightCS,
                            PropertyIDs.cullingClipToWorldMatrix,
                            data.clipToWorld);

                        int threadGroup = (clusterCount + 63) / 64;
                        int threadGroupY =
                            (threadGroup + data.clusterSize.y - 1) / data.clusterSize.y;

                        ctx.cmd.DispatchCompute(
                            data.clusterCullingLightCS,
                            data.clusterCullingLightKernel,
                            data.clusterSize.y,
                            threadGroupY,
                            1);

                        ConstantBuffer.PushGlobal(
                            ctx.cmd,
                            data.clusterCullingLightParams,
                            PropertyIDs.clusterCullingLightParamsBuffer);
                    });
            }
        }

        /// <inheritdoc />
        public override void Cleanup()
        {
            // No disposable resources held by this pass.
        }

        // ── Helpers ──

        /// <summary>
        /// Computes the cluster grid dimensions for the current frame based on
        /// screen resolution.
        /// </summary>
        /// <param name="screenResolution">The screen resolution in pixels.</param>
        /// <returns>The cluster size in X, Y, and Z dimensions.</returns>
        private static int3 GetClusterSize(int2 screenResolution)
        {
            int2 clusterSizeXY = new int2(1, 1);
            int sliceCount = CLUSTER_MIN_Z_SLIZE;
            int tileWidth = 8 >> 1;
            do
            {
                tileWidth <<= 1;
                clusterSizeXY = (screenResolution + tileWidth - 1) / Mathf.Max(1, tileWidth - 1);
                int tileCountPerSlice = clusterSizeXY.x * clusterSizeXY.y;
                sliceCount = MAX_CLUSTER_MASK_WORDS / Mathf.Max(1, tileCountPerSlice - 1);
            }
            while (sliceCount < CLUSTER_MIN_Z_SLIZE
                   || sliceCount > CLUSTER_MAX_Z_SLICE);
            return new int3(clusterSizeXY.x, clusterSizeXY.y, sliceCount);
        }

        /// <summary>
        /// Computes the Z-axis scale and offset for the cluster grid,
        /// with different formulas for orthographic and perspective cameras.
        /// </summary>
        /// <param name="clusterSize">The cluster grid dimensions.</param>
        /// <param name="isOrthographic">
        /// Whether the camera is orthographic.</param>
        /// <param name="nearClipPlane">The camera's near clip plane.</param>
        /// <param name="farClipPlane">The camera's far clip plane.</param>
        /// <returns>
        /// A <see cref="float2"/> containing the Z scale (x) and offset (y).
        /// </returns>
        private static float2 GetClusterZScaleOffset(
            int3 clusterSize,
            bool isOrthographic,
            float nearClipPlane,
            float farClipPlane)
        {
            float2 scaleOffset = new float2(0, 0);
            if (isOrthographic)
            {
                scaleOffset.x =
                    (float)clusterSize.z / (farClipPlane - nearClipPlane);
                scaleOffset.y = -nearClipPlane * scaleOffset.x;
            }
            else
            {
                scaleOffset.x =
                    (float)clusterSize.z
                    / (math.log2(farClipPlane) - math.log2(nearClipPlane));
                scaleOffset.y = -math.log2(nearClipPlane) * scaleOffset.x;
            }

            return scaleOffset;
        }

        // ── Pass data ──

        /// <summary>
        /// Render graph pass data container for
        /// <see cref="ClusterCullingLightPass"/>.
        /// </summary>
        private sealed class ClusterCullingLightPassData
        {
            /// <summary>
            /// The light data compute buffer handle (input from
            /// <see cref="BuildLightDataPass"/>).
            /// </summary>
            public ComputeBufferHandle lightDatasBuffer;

            /// <summary>
            /// The cluster culling light mask buffer handle (output).
            /// </summary>
            public ComputeBufferHandle clusterCullingLightMaskBuffer;

            /// <summary>
            /// The cluster culling compute shader.
            /// </summary>
            public ComputeShader clusterCullingLightCS;

            /// <summary>
            /// The kernel index for the cluster culling dispatch.
            /// </summary>
            public int clusterCullingLightKernel;

            /// <summary>
            /// Global constant buffer parameters for cluster culling light.
            /// </summary>
            public ClusterCullingLightParams clusterCullingLightParams;

            /// <summary>
            /// The cluster grid dimensions for this frame.
            /// </summary>
            public int3 clusterSize;

            /// <summary>
            /// The Z-axis scale (x) and offset (y) for cluster depth slices.
            /// </summary>
            public float2 clusterZScaleOffset;

            /// <summary>
            /// Number of uint words per cluster mask.
            /// </summary>
            public int wordsPerCluster;

            /// <summary>
            /// The number of lights culled this frame.
            /// </summary>
            public int catchedLightCount;

            /// <summary>
            /// Whether the current camera is orthographic.
            /// </summary>
            public bool cameraOrthographic;

            /// <summary>
            /// The clip-to-view matrix.
            /// </summary>
            public Matrix4x4 clipToView;

            /// <summary>
            /// The view-to-clip matrix.
            /// </summary>
            public Matrix4x4 viewToClip;

            /// <summary>
            /// The clip-to-world matrix.
            /// </summary>
            public Matrix4x4 clipToWorld;
        }

        // ── Constants ──

        private const int MAX_CLUSTER_MASK_WORDS = 4096 * 4;
        private const int CLUSTER_MIN_Z_SLIZE = 16;
        private const int CLUSTER_MAX_Z_SLICE = 128;
        private const string CLUSTER_CULLING_CS_KERNEL_NAME =
            "ClusterCullingLightCS";

        // ── Data structures ──

        /// <summary>
        /// GPU-side constant buffer layout for cluster culling light parameters.
        /// Must match the layout declared in the compute shader.
        /// </summary>
        public unsafe struct ClusterCullingLightParams
        {
            /// <summary>The cluster grid dimensions in X and Y.</summary>
            public Vector2 clusterSize;

            /// <summary>
            /// The Z-axis scale (x) and offset (y) for cluster depth slices.
            /// </summary>
            public Vector2 clusterZScaleOffset;

            /// <summary>Number of uint words per cluster mask.</summary>
            public int wordsPerCluster;

            /// <summary>Number of directional lights (excluding main).</summary>
            public int directionalLightCount;

            /// <summary>Number of point and spot lights.</summary>
            public int localLightCount;

            /// <summary>Padding to maintain 16-byte alignment.</summary>
            public float unused;
        }

        /// <summary>
        /// Shader property identifiers used by this pass and its consumers.
        /// </summary>
        public static class PropertyIDs
        {
            /// <summary>
            /// Shader property ID for the cluster culling light mask buffer.
            /// Value: <c>_ClusterCullingLightMaskBuffer</c>.
            /// </summary>
            public static readonly int clusterCullingLightMaskBuffer =
                Shader.PropertyToID("_ClusterCullingLightMaskBuffer");

            /// <summary>
            /// Shader property ID for the cluster culling light params constant buffer.
            /// Value: <c>_ClusterCullingLightParamsBuffer</c>.
            /// </summary>
            public static readonly int clusterCullingLightParamsBuffer =
                Shader.PropertyToID("_ClusterCullingLightParamsBuffer");

            /// <summary>
            /// Shader property ID for culling params 0 (zScale, zOffset,
            /// wordsPerCluster, isOrthographic).
            /// Value: <c>_ClusterCullingLightParams0</c>.
            /// </summary>
            public static readonly int cullingParams0 =
                Shader.PropertyToID("_ClusterCullingLightParams0");

            /// <summary>
            /// Shader property ID for culling params 1 (clusterSizeX, clusterSizeY,
            /// clusterSizeZ, visibleLightCount).
            /// Value: <c>_ClusterCullingLightParams1</c>.
            /// </summary>
            public static readonly int cullingParams1 =
                Shader.PropertyToID("_ClusterCullingLightParams1");

            /// <summary>
            /// Shader property ID for the clip-to-view matrix.
            /// Value: <c>_ClusterCullingLightClipToView</c>.
            /// </summary>
            public static readonly int cullingClipToViewMatrix =
                Shader.PropertyToID("_ClusterCullingLightClipToView");

            /// <summary>
            /// Shader property ID for the view-to-clip matrix.
            /// Value: <c>_ClusterCullingLightViewToClip</c>.
            /// </summary>
            public static readonly int cullingViewToClipMatrix =
                Shader.PropertyToID("_ClusterCullingLightViewToClip");

            /// <summary>
            /// Shader property ID for the clip-to-world matrix.
            /// Value: <c>_ClusterCullingLightClipToWorld</c>.
            /// </summary>
            public static readonly int cullingClipToWorldMatrix =
                Shader.PropertyToID("_ClusterCullingLightClipToWorld");
        }
    }
}
