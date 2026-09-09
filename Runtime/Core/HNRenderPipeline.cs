using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// HNRP 自定义渲染管线实现。
    /// 使用每个相机的 <see cref="CameraRenderer"/> 与共享的 <see cref="RenderGraph"/>
    /// 实例。每个相机通过优先级链选择自己的 <see cref="RenderGraphAsset"/>：
    /// <c>pipelineConfigOverride ?? defaultXxxRenderGraph ?? null</c>。
    /// </summary>
    public class HNRenderPipeline : RenderPipeline
    {
        /// <summary>
        /// 拥有此管线实例的管线资产。
        /// 与静态 <see cref="Asset"/>（读取 <see cref="GraphicsSettings.currentRenderPipeline"/>）
        /// 不同，该实例引用在构造时设置，在 EditMode 测试中也可用。
        /// </summary>
        public static HNRenderPipelineAsset InstanceAsset;

        /// <summary>
        /// 在所有主相机之前渲染实时反射探针。拥有池化的探针相机；
        /// 经 <see cref="Dispose(bool)"/> 释放。
        /// </summary>
        private readonly ReflectionProbeRenderer reflectionProbeRenderer;

        private RuntimeReflectionSystem runtimeReflectionSystem;

        /// <summary>
        /// 当前正通过 <see cref="ReflectionProbe.RenderProbe(RenderTexture)"/>
        /// （编辑器烘焙/自定义流程）烘焙的反射探针。编辑器在调用 <c>RenderProbe</c>
        /// 前立即设置，结束后清除；使触发的反射相机渲染能选择该探针的渲染图视图，
        /// 而非临时相机自身的默认视图索引。
        /// </summary>
        public static ReflectionProbe BakingReflectionProbe { get; set; }

        /// <summary>
        /// 初始化 <see cref="HNRenderPipeline"/> 的新实例。
        /// </summary>
        /// <param name="asset">提供配置的管线资产。</param>
        public HNRenderPipeline(HNRenderPipelineAsset asset)
        {
            InstanceAsset = asset;

            SetSupportedRenderingFeatures();

            reflectionProbeRenderer = new ReflectionProbeRenderer(new ReflectionProbeCameraPool());

            // 在构建任何渲染图前注册全部带 [Pass] 的 pass 类型。
            // Editor 用反射扫描程序集；Player 构建使用 PassRegistryGenerator
            // 生成的硬编码表。
            PassRegistry.RegisterAll();

            GraphicsSettings.lightsUseLinearIntensity = QualitySettings.activeColorSpace == ColorSpace.Linear;
            GraphicsSettings.lightsUseColorTemperature = true;
            GraphicsSettings.defaultRenderingLayerMask = defaultRenderingLayerMask;
            GraphicsSettings.useScriptableRenderPipelineBatching = true;

            try
            {
                Blitter.Initialize(
                    asset.runtimeResources.shaderResources.Blit,
                    asset.runtimeResources.shaderResources.BlitColorAndDepth);
            }
            catch (Exception)
            {
                // Blitter 可能已初始化（例如之前的测试管线）。可安全忽略。
            }

            RTHandles.SetReferenceSize(Screen.width, Screen.height);

            // RTHandles.Initialize 已过时且只能调用一次（任何 RTHandle 分配之前）。
            // 用静态标志保护，使重复的管线构造（如 EditMode 测试创建多个
            // HNRenderPipeline，或 Editor 渲染循环正在分配瞬时目标）不触发
            // "should only be called once" 错误。逐帧尺寸由 Render() 内的
            // RTHandles.SetReferenceSize 处理。
            if (!rtHandlesInitialized)
            {
                RTHandles.Initialize(Screen.width, Screen.height);
                rtHandlesInitialized = true;
            }
        }

        /// <inheritdoc />
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            Render(context, new List<Camera>(cameras));
        }

        /// <inheritdoc />
        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            BeginContextRendering(context, cameras);

#if UNITY_EDITOR
            if (globalSettings == null || HNRenderPipelineGlobalSettings.Instance == null)
            {
                globalSettings = HNRenderPipelineGlobalSettings.Ensure();
                if (globalSettings == null)
                    return;

                RenderGraphTemplates.EnsureAll();
            }
#endif

            var cmd = CommandBufferPool.Get("HNRP");
            var parameters = new RenderGraphParameters
            {
                currentFrameIndex = Time.frameCount,
                executionName = "HNRP",
                scriptableRenderContext = context,
                commandBuffer = cmd,
                rendererListCulling = false,
            };

            if (runtimeReflectionSystem == null)
            {
                runtimeReflectionSystem = new RuntimeReflectionSystem();
                ScriptableRuntimeReflectionSystemSettings.system = runtimeReflectionSystem;
            }

            // ── 阶段 A：裁剪每个相机，收集实时探针 ──
            // 实时反射探针在所有主相机之前渲染，因此先裁剪每个相机
            // （结果缓存在 CameraContext），并收集可见的实时探针。CullingResults
            // 在本 Render 调用剩余时间内有效，并在阶段 C 复用。
            var cameraContexts = new List<CameraContext>();
            var selectedGraphs = new Dictionary<Camera, RenderGraphAsset>();
            reflectionProbeRenderer.BeginFrame();

            foreach (Camera camera in cameras)
            {
                // ── 选择 RenderGraphAsset ──
                RenderGraphAsset renderGraphAsset;

                if (camera.cameraType == CameraType.Preview)
                {
                    string cameraName = camera.name;
                    if (cameraName == HNRenderPipelineUtils.PREVIEW_CAMERA_NAME)
                    {
                        renderGraphAsset = InstanceAsset.gameViewRenderGraphViewBlock.GetRenderGraphObject();
                    }
                    else if (cameraName == HNRenderPipelineUtils.PREVIEW_SCENE_CAMERA_NAME)
                    {
                        renderGraphAsset = InstanceAsset.previewRenderGraphViewBlock.GetRenderGraphObject();
                    }
                    else
                    {
                        if (camera.TryGetComponent<HNRenderpipelinePreviewCameraSettings>(out var previewCameraSettings))
                        {
                            renderGraphAsset = previewCameraSettings.GetPreviewCameraGraphView();
                        }
                        else
                        {
                            renderGraphAsset = InstanceAsset.previewRenderGraphViewBlock.GetRenderGraphObject();
                        }
                    }
                }

                if (camera.cameraType == CameraType.Reflection)
                {
                    // Bake/custom 触发的反射相机（ReflectionProbe.RenderProbe）：
                    // 不经过主相机的实时探针收集，直接按烘焙中探针的
                    // render graph view 渲染。临时相机不挂 HNAdditionalCameraData。
                    renderGraphAsset = ReflectionProbeRenderUtils.SelectReflectionRenderGraph(
                        InstanceAsset, BakingReflectionProbe);
                }
                else
                {
                    var cameraData = camera.GetHNRPAdditionalCameraData();
                    renderGraphAsset = SelectPipelineConfig(camera, cameraData);
                }

                if (renderGraphAsset == null)
                    continue;

                // ── 创建 CameraContext ──
                var cameraContext = new CameraContext(camera, context)
                {
                    Flip = SystemInfo.graphicsUVStartsAtTop
                        && camera.cameraType != CameraType.SceneView
                        && camera.cameraType != CameraType.Preview,
                    RuntimeResources = InstanceAsset.runtimeResources,
                };

                // ── 裁剪 ──
                bool gotParams = camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParams);
                if (gotParams)
                {
                    cameraContext.CullingResults = context.Cull(ref cullingParams);
                    cameraContext.HasCullingResults = true;

                    // 填充可见光数组，供光照 pass（如 BuildLightDataPass）消费。
                    // CameraContext.Dispose 释放该数组。
                    cameraContext.VisibleLights = new NativeArray<UnityEngine.Rendering.VisibleLight>(
                        cameraContext.CullingResults.visibleLights, Allocator.TempJob);

                    if (camera.cameraType != CameraType.Reflection)
                    {
                        cameraContext.VisibleReflectionProbes =
                            new NativeArray<UnityEngine.Rendering.VisibleReflectionProbe>(
                                cameraContext.CullingResults.visibleReflectionProbes, Allocator.TempJob);

                        // 收集本相机可见的实时探针。
                        reflectionProbeRenderer.CollectReflectionProbes(cameraContext.VisibleReflectionProbes);
                    }
                }

                cameraContexts.Add(cameraContext);
                selectedGraphs[camera] = renderGraphAsset;
            }

            // ── 阶段 B：在所有主相机之前渲染实时探针 ──
            // 每个探针面在独立的 RecordAndExecute 块内执行（RenderProbes 内部），
            // 使每面相机矩阵在其 pass 运行时处于激活状态。
            reflectionProbeRenderer.RenderProbes(context, renderGraph, parameters, InstanceAsset);

            // 把所有相机 pass 记录进图；RenderGraphExecution.Dispose()
            //（本块结束处）编译并执行记录的 pass。
            using (renderGraph.RecordAndExecute(parameters))
            {
                // ── 阶段 C：用缓存的裁剪结果渲染主相机 ──
                foreach (CameraContext cameraContext in cameraContexts)
                {
                    Camera camera = cameraContext.Camera;
                    if (!selectedGraphs.TryGetValue(camera, out RenderGraphAsset renderGraphAsset) ||
                        renderGraphAsset == null)
                        continue;

                    // ── 每相机设置 ──
                    RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);

                    if (camera.targetTexture != null)
                    {
                        camera.targetTexture.IncrementUpdateCount();
                    }

#if UNITY_EDITOR
                    if (camera.cameraType == CameraType.SceneView)
                    {
                        ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                    }
#endif

                    SetupCameraProperties(context, camera);

                    var globalConstantBuffer = new GlobalConstantBuffer();
                    GlobalConstantBufferUtility.FillFromCamera(
                        camera,
                        true,
                        ref globalConstantBuffer);
                    ConstantBuffer.PushGlobal(
                        cmd,
                        globalConstantBuffer,
                        GlobalPropertyIDs.ShaderVariablesGlobal);

                    // ── 创建 CameraRenderer，从模板构建，渲染 ──
                    var cameraRenderer = new CameraRenderer(cameraContext);
                    cameraRenderer.Build(renderGraphAsset);

                    BeginCameraRendering(context, camera);
                    cameraRenderer.Render(renderGraph, context);
                    EndCameraRendering(context, camera);

                    // cameraContext.Dispose() 推迟到 RecordAndExecute 块之后：
                    // 渲染图在块结束时编译执行记录的 pass，BuildLightData
                    // job（读取 VisibleLights）必须在其原生数组释放前完成。
                }
            }

            // ── 执行所有已记录的渲染图 pass ──
            renderGraph.EndFrame();

#if UNITY_EDITOR
            // HNRP 物体渲染（DrawObjectPass）用 Y 翻转投影矩阵（renderIntoTexture=true），
            // 把全局 unity_MatrixVP 设成翻转矩阵。Unity Editor 内部绘制 SceneView 的
            // grid（xz 网格平面）依赖非翻转的全局 unity_MatrixVP，需在此恢复，否则 grid 颠倒。
            foreach (CameraContext cameraContext in cameraContexts)
            {
                Camera camera = cameraContext.Camera;
                if (camera.cameraType == CameraType.SceneView)
                {
                    cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                    break;
                }
            }
#endif

            // ── 执行后清理每个相机的上下文 ──
            foreach (CameraContext cameraContext in cameraContexts)
            {
                cameraContext.Dispose();
            }

            reflectionProbeRenderer.EndFrame();

            // RenderGraph 把命令记录进命令缓冲；提交到上下文。
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.Submit();

            CommandBufferPool.Release(cmd);

            EndContextRendering(context, cameras);
        }

        /// <summary>
        /// 按优先级链为相机选择 <see cref="RenderGraphAsset"/>：
        /// <c>pipelineConfigOverride ?? defaultXxxRenderGraph ?? null</c>。
        /// </summary>
        /// <param name="camera">正在渲染的相机。</param>
        /// <param name="cameraData">相机的附加数据组件。</param>
        /// <returns>
        /// 选中的 <see cref="RenderGraphAsset"/>，无可用渲染图时返回 <c>null</c>
        /// （该相机将被跳过）。
        /// </returns>
        public RenderGraphAsset SelectPipelineConfig(Camera camera, HNAdditionalCameraData cameraData)
        {
            // 第 1 步：按相机类型取渲染图视图块
            RenderGraphViewBlock viewBlock = GetViewBlockForCameraType(camera.cameraType);
            if (viewBlock == null)
                return null;

            // 第 2 步：从视图块按索引取渲染图
            int index = cameraData.RenderGraphViewIndex;
            RenderGraphAsset config = viewBlock.GetRenderGraphObject(index);
            if (config != null)
                return config;

            // 第 3 步：回退到视图块中的第一个渲染图
            return viewBlock.GetRenderGraphObject();
        }

        /// <summary>
        /// 从 <see cref="HNRenderPipelineAsset"/> 按相机类型取对应的
        /// <see cref="RenderGraphViewBlock"/>。
        /// </summary>
        /// <param name="cameraType">相机的类型。</param>
        /// <returns>给定相机类型的视图块，找不到时返回 <c>null</c>。</returns>
        private RenderGraphViewBlock GetViewBlockForCameraType(CameraType cameraType)
        {
            return cameraType switch
            {
                CameraType.Game => InstanceAsset.gameViewRenderGraphViewBlock,
#if UNITY_EDITOR
                CameraType.SceneView => InstanceAsset.sceneViewRenderGraphViewBlock,
                CameraType.Preview => InstanceAsset.previewRenderGraphViewBlock,
#endif
                CameraType.Reflection => InstanceAsset.reflectionRenderGraphViewBlock,
                _ => null,
            };
        }

        /// <summary>
        /// 设置本管线支持的渲染特性。
        /// </summary>
        private static void SetSupportedRenderingFeatures()
        {
#if UNITY_EDITOR
            SupportedRenderingFeatures.active = new SupportedRenderingFeatures()
            {
                // reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.Rotation,
                // defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly,
                // mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.IndirectOnly,
                // lightmapBakeTypes = LightmapBakeType.Baked | LightmapBakeType.Mixed | LightmapBakeType.Realtime,
                // lightmapsModes = LightmapsMode.NonDirectional | LightmapsMode.CombinedDirectional,
                // lightProbeProxyVolumes = true,
                // motionVectors = true,
                receiveShadows = false,
                // reflectionProbes = true,
                // rendererPriority = true,
                // overridesFog = true,
                // overridesOtherLightingSettings = true,
                // editableMaterialRenderQueue = false,
                // enlighten = true,
                // overridesLODBias = true,
                // overridesMaximumLODLevel = true,
                // overridesShadowmask = true,
                // overridesRealtimeReflectionProbes = true,
                // autoAmbientProbeBaking = true,
                // autoDefaultReflectionProbeBaking = false,
                // rendersUIOverlay = true,
                // supportsHDR = true
            };
#endif
        }

        /// <summary>
        /// 逐帧设置相机属性（VP 矩阵等）到渲染上下文。
        /// </summary>
        /// <param name="context">ScriptableRenderContext。</param>
        /// <param name="camera">要设置的相机。</param>
        private static void SetupCameraProperties(ScriptableRenderContext context, Camera camera)
        {
            var cmd = CommandBufferPool.Get("CameraSetup");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);

            context.SetupCameraProperties(camera);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            Blitter.Cleanup();

            Graphics.SetRenderTarget(null);

            renderGraph.Cleanup();
            renderGraph = null;

            reflectionProbeRenderer.Dispose();

            ConstantBuffer.ReleaseAll();
        }

        /// <summary>
        /// 获取当前管线资产。便捷访问器。
        /// </summary>
        public static HNRenderPipelineAsset Asset
        {
            get => GraphicsSettings.currentRenderPipeline as HNRenderPipelineAsset;
        }

        /// <summary>
        /// 所有相机共享的 <see cref="RenderGraph"/> 实例。
        /// 每帧所有相机在 <c>RecordAndExecute</c> 块内把 pass 记录进该图；
        /// <see cref="RenderGraph.EndFrame"/> 在之后释放帧资源。
        /// </summary>
        internal RenderGraph renderGraph = new RenderGraph("HNRP");

        /// <inheritdoc />
        public override RenderPipelineGlobalSettings defaultSettings => globalSettings;

        private HNRenderPipelineGlobalSettings globalSettings;

        /// <summary>
        /// 保护一次性 <see cref="RTHandles.Initialize"/> 调用。细节见构造函数。
        /// </summary>
        private static bool rtHandlesInitialized;

        internal const int defaultRenderingLayerMask = 0x00000001;
    }
}
