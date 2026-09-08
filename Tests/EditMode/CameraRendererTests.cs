// <copyright file="CameraRendererTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

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
    /// Tests for <see cref="CameraRenderer"/> in <c>Runtime/Core/CameraRenderer.cs</c>.
    /// Verifies pass building, add/remove, config copying, enable/disable toggling,
    /// and template reset.
    /// </summary>
    public sealed class CameraRendererTests
    {
        #region Test Pass Subclasses

        /// <summary>
        /// Minimal pass for renderer tests. Tracks lifecycle invocations.
        /// Registered as <c>"CameraRendererTestPass"</c>.
        /// </summary>
        [Pass("CameraRendererTestPass")]
        private sealed class TestPass : Pass
        {
            public bool SetupSlotsCalled { get; private set; }
            public bool InitializeCalled { get; private set; }
            public bool RecordCalled { get; private set; }
            public bool CleanupCalled { get; private set; }

            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestPass(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor required by <see cref="CameraRenderer.AddPass{T}"/>.
            /// </summary>
            public TestPass()
                : base("CameraRendererTestPass")
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
            public override void Cleanup()
            {
                base.Cleanup();
                CleanupCalled = true;
            }

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        #endregion

        #region Setup

        /// <summary>
        /// Ensures <see cref="PassRegistry"/> is populated before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        #endregion

        #region Build — From Template

        /// <summary>
        /// <see cref="CameraRenderer.Build"/> instantiates passes from a
        /// <see cref="RenderGraphAsset"/> template and populates the pass list.
        /// </summary>
        [Test]
        public void Build_FromTemplate_InstantiatesPasses()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPass("PassA"));
            asset.Passes.Add(new TestPass("PassB"));

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var ctx = new CameraContext(camera, default);
            var renderer = new CameraRenderer(ctx);

            try
            {
                renderer.Build(asset);

                Assert.That(renderer.Passes, Is.Not.Null,
                    "Pass list should be non-null after Build.");
                Assert.That(renderer.Passes.Count, Is.EqualTo(2),
                    "Build should create two passes from two templates.");
                Assert.That(renderer.Passes[0].PassName, Is.EqualTo("PassA"),
                    "First pass should use the PassName from its template.");
                Assert.That(renderer.Passes[1].PassName, Is.EqualTo("PassB"),
                    "Second pass should use the PassName from its template.");
                Assert.That(renderer.CurrentTemplate, Is.SameAs(asset),
                    "CurrentTemplate should reference the built asset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                ctx.Dispose();
            }
        }

        #endregion

        #region AddPass

        /// <summary>
        /// <see cref="CameraRenderer.AddPass{T}"/> creates and appends a new pass
        /// to the pass list with the correct name and type.
        /// </summary>
        [Test]
        public void AddPass_AppendsToList()
        {
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var ctx = new CameraContext(camera, default);
            var renderer = new CameraRenderer(ctx);

            try
            {
                Assert.That(renderer.Passes.Count, Is.EqualTo(0),
                    "Pass list should be empty before AddPass.");

                TestPass added = renderer.AddPass<TestPass>("MyAddedPass");

                Assert.That(added, Is.Not.Null,
                    "AddPass should return a non-null pass.");
                Assert.That(added.PassName, Is.EqualTo("MyAddedPass"),
                    "Added pass should have the specified name.");
                Assert.That(added.GetType(), Is.EqualTo(typeof(TestPass)),
                    "Added pass should be of the requested type.");
                Assert.That(renderer.Passes.Count, Is.EqualTo(1),
                    "Pass list should contain exactly one pass after AddPass.");
                Assert.That(renderer.Passes[0], Is.SameAs(added),
                    "The returned pass should be the same instance in the list.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                ctx.Dispose();
            }
        }

        #endregion

        #region RemovePass

        /// <summary>
        /// <see cref="CameraRenderer.RemovePass"/> removes a pass by name.
        /// Removing a non-existent name is a no-op.
        /// </summary>
        [Test]
        public void RemovePass_ByName_Works()
        {
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var ctx = new CameraContext(camera, default);
            var renderer = new CameraRenderer(ctx);

            try
            {
                renderer.AddPass<TestPass>("KeepMe");
                renderer.AddPass<TestPass>("DeleteMe");
                renderer.AddPass<TestPass>("AlsoKeep");

                Assert.That(renderer.Passes.Count, Is.EqualTo(3),
                    "Should have three passes before removal.");

                renderer.RemovePass("DeleteMe");

                Assert.That(renderer.Passes.Count, Is.EqualTo(2),
                    "Should have two passes after removing one.");
                Assert.That(renderer.Passes.Exists(p => p.PassName == "KeepMe"), Is.True,
                    "KeepMe should still be in the list.");
                Assert.That(renderer.Passes.Exists(p => p.PassName == "AlsoKeep"), Is.True,
                    "AlsoKeep should still be in the list.");
                Assert.That(renderer.Passes.Exists(p => p.PassName == "DeleteMe"), Is.False,
                    "DeleteMe should not be in the list.");

                // Removing a non-existent name should not throw.
                Assert.DoesNotThrow(() => renderer.RemovePass("DoesNotExist"),
                    "Removing a non-existent pass should not throw.");
                Assert.That(renderer.Passes.Count, Is.EqualTo(2),
                    "Pass count should be unchanged after removing non-existent pass.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                ctx.Dispose();
            }
        }

        #endregion

        #region SetPassEnabled — Toggle Execution

        /// <summary>
        /// <see cref="CameraRenderer.SetPassEnabled"/> toggles a pass's
        /// <see cref="Pass.IsEnabled"/> flag, and <see cref="CameraRenderer.Render"/>
        /// skips disabled passes.
        /// </summary>
        [Test]
        public void SetPassEnabled_TogglesExecution()
        {
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var ctx = new CameraContext(camera, default);
            var renderer = new CameraRenderer(ctx);

            try
            {
                var passA = renderer.AddPass<TestPass>("PassA");
                var passB = renderer.AddPass<TestPass>("PassB");

                // Simulate build-time topology setup: slots are declared once
                // (Render does NOT call SetupSlots per frame anymore).
                passA.SetupSlots();
                passB.SetupSlots();

                // Disable PassB.
                renderer.SetPassEnabled("PassB", false);

                Assert.That(passA.IsEnabled, Is.True,
                    "PassA should remain enabled.");
                Assert.That(passB.IsEnabled, Is.False,
                    "PassB should be disabled after SetPassEnabled(false).");

                // Render: PassB should be skipped.
                renderer.Render(null, default);

                Assert.That(passA.InitializeCalled, Is.True,
                    "Enabled pass should have Initialize called.");
                Assert.That(passA.RecordCalled, Is.True,
                    "Enabled pass should have Record called.");

                Assert.That(passB.InitializeCalled, Is.False,
                    "Disabled pass should NOT have Initialize called.");
                Assert.That(passB.RecordCalled, Is.False,
                    "Disabled pass should NOT have Record called.");

                // Both passes should be cleaned up regardless.
                Assert.That(passA.CleanupCalled, Is.True,
                    "Enabled pass should have Cleanup called.");
                Assert.That(passB.CleanupCalled, Is.True,
                    "Disabled pass should also have Cleanup called.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                ctx.Dispose();
            }
        }

        #endregion

        #region Reset — Restore from Template

        /// <summary>
        /// <see cref="CameraRenderer.Reset"/> clears runtime state and rebuilds
        /// from a new template, restoring the template-defined state.
        /// </summary>
        [Test]
        public void Reset_RestoresTemplateState()
        {
            // First template: one pass.
            var asset1 = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset1.Passes.Add(new TestPass("Pass1"));

            // Second template: two passes.
            var asset2 = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset2.Passes.Add(new TestPass("Alpha"));
            asset2.Passes.Add(new TestPass("Beta"));

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var ctx = new CameraContext(camera, default);
            var renderer = new CameraRenderer(ctx);

            try
            {
                // Build from first template, then manually add a pass.
                renderer.Build(asset1);
                renderer.AddPass<TestPass>("ManualPass");

                Assert.That(renderer.Passes.Count, Is.EqualTo(2),
                    "Should have 2 passes: 1 from template + 1 manual.");
                Assert.That(renderer.CurrentTemplate, Is.SameAs(asset1),
                    "CurrentTemplate should be asset1 before reset.");

                // Reset to second template — should discard everything and rebuild.
                renderer.Reset(asset2);

                Assert.That(renderer.Passes.Count, Is.EqualTo(2),
                    "Reset should rebuild from the new template, " +
                    "discarding the manual pass.");
                Assert.That(renderer.Passes[0].PassName, Is.EqualTo("Alpha"),
                    "First pass should be from the new template.");
                Assert.That(renderer.Passes[1].PassName, Is.EqualTo("Beta"),
                    "Second pass should be from the new template.");
                Assert.That(renderer.CurrentTemplate, Is.SameAs(asset2),
                    "CurrentTemplate should be updated to asset2 after reset.");

                // The manual pass should NOT be in the list.
                Assert.That(renderer.Passes.Exists(p => p.PassName == "ManualPass"), Is.False,
                    "Manual pass should be discarded by Reset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset1);
                UnityEngine.Object.DestroyImmediate(asset2);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                ctx.Dispose();
            }
        }

        #endregion
    }
}
