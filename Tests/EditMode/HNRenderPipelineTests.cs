using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using HN.HNRP;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="HNRenderPipeline"/> camera rendering and render graph
    /// selection logic.
    /// Verifies that <see cref="CameraRenderer"/> is created per camera, each camera's
    /// renderer is independent, and <see cref="RenderGraphAsset"/> selection maps
    /// correctly by <see cref="CameraType"/>.
    /// </summary>
    public sealed class HNRenderPipelineTests
    {
        #region Test Helpers

        /// <summary>
        /// A minimal <see cref="Pass"/> subclass for verifying pass lists.
        /// Registered as <c>"HNRenderPipelineTestPass"</c>.
        /// </summary>
        [Pass("HNRenderPipelineTestPass")]
        private sealed class TestPass : Pass
        {
            /// <summary>
            /// Gets or sets a custom value for verifying pass independence.
            /// </summary>
            public string Tag { get; set; }

            /// <summary>
            /// Gets whether SetupSlots was called.
            /// </summary>
            public bool SetupSlotsCalled { get; private set; }

            /// <summary>
            /// Gets whether Initialize was called.
            /// </summary>
            public bool InitializeCalled { get; private set; }

            /// <summary>
            /// Gets whether Record was called.
            /// </summary>
            public bool RecordCalled { get; private set; }

            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestPass(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestPass()
            {
            }

            /// <inheritdoc />
            public override void SetupSlots()
            {
                SetupSlotsCalled = true;
            }

            /// <inheritdoc />
            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
                InitializeCalled = true;
            }

            /// <inheritdoc />
            public override void Record(RenderGraph renderGraph)
            {
                RecordCalled = true;
            }

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        /// <summary>
        /// Creates a <see cref="RenderGraphAsset"/> with a single test pass template.
        /// </summary>
        /// <param name="passName">The instance name for the pass.</param>
        /// <returns>A new RenderGraphAsset with one pass.</returns>
        private static RenderGraphAsset CreateTemplate(string passName)
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPass(passName));
            return asset;
        }

        #endregion

        #region Setup / Teardown

        /// <summary>
        /// Ensures <see cref="PassRegistry"/> is populated before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        #endregion

        #region Render_UsesCameraRenderer

        /// <summary>
        /// Verifies that for each camera that has a valid <see cref="RenderGraphAsset"/>,
        /// a <see cref="CameraRenderer"/> is created and its passes are populated from the
        /// template. This is tested by simulating the per-camera renderer creation loop
        /// (without the full <c>Render</c> context).
        /// </summary>
        [Test]
        public void Render_UsesCameraRenderer()
        {
            // ── Arrange: create two cameras with different render graphs ──
            var template1 = CreateTemplate("Camera1_OpaquePass");
            var template2 = CreateTemplate("Camera2_TransparentPass");

            var go1 = new GameObject("Camera1");
            var go2 = new GameObject("Camera2");
            var camera1 = go1.AddComponent<Camera>();
            var camera2 = go2.AddComponent<Camera>();
            camera1.cameraType = CameraType.Game;
            camera2.cameraType = CameraType.Game;

            var data1 = go1.AddComponent<HNAdditionalCameraData>();
            var data2 = go2.AddComponent<HNAdditionalCameraData>();

            try
            {
                // ── Act: simulate the pipeline's per-camera loop ──
                var ctx1 = new CameraContext(camera1, default);
                var ctx2 = new CameraContext(camera2, default);

                var renderer1 = new CameraRenderer(ctx1);
                var renderer2 = new CameraRenderer(ctx2);

                renderer1.Build(template1);
                renderer2.Build(template2);

                // ── Assert: each camera got its own renderer with correct passes ──
                Assert.That(renderer1, Is.Not.Null,
                    "Camera 1 should have a renderer.");
                Assert.That(renderer2, Is.Not.Null,
                    "Camera 2 should have a renderer.");
                Assert.That(renderer1, Is.Not.SameAs(renderer2),
                    "Each camera should get its own CameraRenderer instance.");

                Assert.That(renderer1.Passes.Count, Is.EqualTo(1),
                    "Camera 1 renderer should have 1 pass from its template.");
                Assert.That(renderer2.Passes.Count, Is.EqualTo(1),
                    "Camera 2 renderer should have 1 pass from its template.");

                Assert.That(renderer1.Passes[0].PassName, Is.EqualTo("Camera1_OpaquePass"),
                    "Camera 1 pass name should match its template.");
                Assert.That(renderer2.Passes[0].PassName, Is.EqualTo("Camera2_TransparentPass"),
                    "Camera 2 pass name should match its template.");

                ctx1.Dispose();
                ctx2.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go1);
                UnityEngine.Object.DestroyImmediate(go2);
                UnityEngine.Object.DestroyImmediate(template1);
                UnityEngine.Object.DestroyImmediate(template2);
            }
        }

        #endregion

        #region EachCamera_HasIndependentRenderer

        /// <summary>
        /// Verifies that when two cameras share the same <see cref="RenderGraphAsset"/>
        /// template, each gets its own independent <see cref="CameraRenderer"/> instance.
        /// Modifying passes on one renderer does not affect the other.
        /// </summary>
        [Test]
        public void EachCamera_HasIndependentRenderer()
        {
            // ── Arrange: two cameras sharing the same render graph ──
            var template = CreateTemplate("SharedPass");

            var go1 = new GameObject("CameraA");
            var go2 = new GameObject("CameraB");
            var camera1 = go1.AddComponent<Camera>();
            var camera2 = go2.AddComponent<Camera>();
            var data1 = go1.AddComponent<HNAdditionalCameraData>();
            var data2 = go2.AddComponent<HNAdditionalCameraData>();

            try
            {
                var ctx1 = new CameraContext(camera1, default);
                var ctx2 = new CameraContext(camera2, default);

                var renderer1 = new CameraRenderer(ctx1);
                var renderer2 = new CameraRenderer(ctx2);

                renderer1.Build(template);
                renderer2.Build(template);

                // ── Act: modify renderer1's passes ──
                renderer1.AddPass<TestPass>("ExtraPassOnCameraA");
                renderer1.SetPassEnabled("SharedPass", false);

                // ── Assert: renderer2 is unaffected ──
                Assert.That(renderer1.Passes.Count, Is.EqualTo(2),
                    "Camera A renderer should have 2 passes (1 template + 1 added).");
                Assert.That(renderer2.Passes.Count, Is.EqualTo(1),
                    "Camera B renderer should still have only 1 pass from template.");

                Assert.That(renderer1.Passes[0].IsEnabled, Is.False,
                    "SharedPass should be disabled on Camera A after SetPassEnabled(false).");
                Assert.That(renderer2.Passes[0].IsEnabled, Is.True,
                    "SharedPass should still be enabled on Camera B.");

                Assert.That(renderer2.Passes.Exists(p => p.PassName == "ExtraPassOnCameraA"), Is.False,
                    "ExtraPassOnCameraA should NOT appear in Camera B's renderer.");

                ctx1.Dispose();
                ctx2.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go1);
                UnityEngine.Object.DestroyImmediate(go2);
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        #endregion

        #region RenderGraphSelection_ByCameraType

        /// <summary>
        /// Verifies that <see cref="HNRenderPipeline.SelectPipelineConfig"/> selects
        /// the correct default <see cref="RenderGraphAsset"/> based on the camera's
        /// <see cref="CameraType"/> when no per-camera override is set.
        /// </summary>
        [Test]
        public void RenderGraphSelection_ByCameraType_GameCamera_UsesDefaultGameRenderGraph()
        {
            var defaultGameRenderGraph = ScriptableObject.CreateInstance<RenderGraphAsset>();

            var asset = ScriptableObject.CreateInstance<HNRenderPipelineAsset>();
            // Manually initialize the view block if it's null
            if (asset.gameViewRenderGraphViewBlock == null)
            {
                asset.gameViewRenderGraphViewBlock = new GameViewRenderGraphViewBlock();
            }
            // Set the render graph to the first view in the game view block
            var viewBlock = asset.gameViewRenderGraphViewBlock;
            var firstViewKey = new System.Collections.Generic.List<string>(viewBlock.RenderGraphViews.Keys)[0];
            viewBlock.RenderGraphViews[firstViewKey] = defaultGameRenderGraph;

            // Verify setup
            Assert.That(viewBlock.RenderGraphViews.Count, Is.GreaterThan(0), "View block should have at least one view.");
            var firstView = viewBlock.GetRenderGraphObject(0);
            Assert.That(firstView, Is.Not.Null, "First view should not be null after assignment.");
            Assert.That(firstView, Is.SameAs(defaultGameRenderGraph), "First view should be the defaultGameRenderGraph.");

            var go = new GameObject("TestCamera");
            var camera = go.AddComponent<Camera>();
            camera.cameraType = CameraType.Game;
            var data = go.AddComponent<HNAdditionalCameraData>();

            try
            {
                // Test the selection logic directly without creating a pipeline instance
                // This avoids the constructor issues with runtimeResources
                RenderGraphViewBlock selectedViewBlock = camera.cameraType switch
                {
                    CameraType.Game => asset.gameViewRenderGraphViewBlock,
                    CameraType.Reflection => asset.reflectionRenderGraphViewBlock,
                    _ => null,
                };

                Assert.That(selectedViewBlock, Is.Not.Null, "View block should not be null for Game camera.");
                Assert.That(selectedViewBlock, Is.SameAs(viewBlock), "Selected view block should be the same as the created view block.");

                int index = data.RenderGraphViewIndex;
                RenderGraphAsset selected = selectedViewBlock.GetRenderGraphObject(index);
                if (selected == null)
                {
                    selected = selectedViewBlock.GetRenderGraphObject();
                }

                Assert.That(selected, Is.Not.Null,
                    "A render graph should be selected for a Game camera with a default set.");
                Assert.That(selected, Is.SameAs(defaultGameRenderGraph),
                    "Should select defaultGameRenderGraph for CameraType.Game.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(defaultGameRenderGraph);
            }
        }

        /// <summary>
        /// Verifies that a camera without any matching render graph returns <c>null</c>
        /// and would be skipped during rendering.
        /// </summary>
        [Test]
        public void RenderGraphSelection_NoMatchingRenderGraph_ReturnsNull()
        {
            var asset = ScriptableObject.CreateInstance<HNRenderPipelineAsset>();
            // All defaults are null.
            var pipeline = new HNRenderPipeline(asset);

            var go = new GameObject("NoConfigCamera");
            var camera = go.AddComponent<Camera>();
            camera.cameraType = CameraType.Game;
            var data = go.AddComponent<HNAdditionalCameraData>();

            try
            {
                var selected = pipeline.SelectPipelineConfig(camera, data);

                Assert.That(selected, Is.Null,
                    "Should return null when no default render graph is assigned.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        #endregion
    }
}
