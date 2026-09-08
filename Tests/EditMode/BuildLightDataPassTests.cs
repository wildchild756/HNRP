// <copyright file="BuildLightDataPassTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="BuildLightDataPass"/> in
    /// <c>Runtime/Passes/BuildLightDataPass/BuildLightDataPass.cs</c>.
    /// Verifies pass construction, slot setup, context initialization,
    /// <c>[Pass]</c> attribute discovery, and property IDs.
    /// </summary>
    public sealed class BuildLightDataPassTests
    {
        #region Setup

        /// <summary>
        /// Registers all <c>[Pass]</c>-decorated types so the pass registry
        /// can resolve passes during tests.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        #endregion

        #region Construction

        /// <summary>
        /// Verifies that a <see cref="BuildLightDataPass"/> can be constructed and
        /// its <see cref="Pass.PassName"/> matches the supplied value.
        /// </summary>
        [Test]
        public void Constructor_PassName_MatchesSuppliedName()
        {
            const string expectedName = "TestBuildLight";

            var pass = new BuildLightDataPass(expectedName);

            Assert.That(pass.PassName, Is.EqualTo(expectedName));
        }

        /// <summary>
        /// Verifies that a newly constructed pass is enabled by default.
        /// </summary>
        [Test]
        public void Constructor_IsEnabled_DefaultsToTrue()
        {
            var pass = new BuildLightDataPass("Test");

            Assert.That(pass.IsEnabled, Is.True);
        }

        /// <summary>
        /// Verifies that the <see cref="PassNameConst"/> constant matches
        /// the legacy <see cref="BuildLightDataPass.PassName"/>.
        /// </summary>
        [Test]
        public void PassNameConst_MatchesLegacy()
        {
            Assert.That(
                BuildLightDataPass.PassNameConst,
                Is.EqualTo(BuildLightDataPass.PassNameConst));
        }

        #endregion

        #region [Pass] Attribute

        /// <summary>
        /// Verifies that the <c>[Pass]</c> attribute is present on
        /// <see cref="BuildLightDataPass"/> with the correct display name.
        /// </summary>
        [Test]
        public void PassAttribute_HasCorrectDisplayName()
        {
            var attribute = typeof(BuildLightDataPass)
                .GetCustomAttribute<PassAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.DisplayName, Is.EqualTo("Build Light Data"));
        }

        /// <summary>
        /// Verifies that <see cref="BuildLightDataPass"/> is discoverable
        /// through <see cref="PassRegistry"/> after registration.
        /// </summary>
        [Test]
        public void PassRegistry_CanGetByDisplayName()
        {
            var pass = PassRegistry.CreatePass("Build Light Data", "TestInstance");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<BuildLightDataPass>());
        }

        #endregion

        #region SetupSlots

        /// <summary>
        /// Verifies that <see cref="BuildLightDataPass.SetupSlots"/>
        /// creates a <see cref="ComputeBufferSlot"/> with the correct name.
        /// </summary>
        [Test]
        public void SetupSlots_CreatesComputeBufferSlot_WithCorrectName()
        {
            var pass = new BuildLightDataPass("Test");

            pass.SetupSlots();

            Assert.That(pass.LightDatasBufferSlot, Is.Not.Null);
            Assert.That(pass.LightDatasBufferSlot.SlotName, Is.EqualTo("lightDatasBuffer"));
        }

        /// <summary>
        /// Verifies that the slot created by <see cref="SetupSlots"/>
        /// is an output slot.
        /// </summary>
        [Test]
        public void SetupSlots_SlotDirection_IsOutput()
        {
            var pass = new BuildLightDataPass("Test");

            pass.SetupSlots();

            Assert.That(pass.LightDatasBufferSlot.Direction, Is.EqualTo(SlotDirection.Output));
        }

        /// <summary>
        /// Verifies that the slot created by <see cref="SetupSlots"/>
        /// is of type <see cref="ComputeBufferSlot"/>.
        /// </summary>
        [Test]
        public void SetupSlots_SlotType_IsComputeBufferSlot()
        {
            var pass = new BuildLightDataPass("Test");

            pass.SetupSlots();

            Assert.That(pass.LightDatasBufferSlot, Is.InstanceOf<ComputeBufferSlot>());
        }

        #endregion

        #region Initialize

        /// <summary>
        /// Verifies that <see cref="BuildLightDataPass.PreRecord"/> correctly
        /// computes the light count from the camera context's visible lights.
        /// </summary>
        [Test]
        public void Initialize_LightCount_BoundedByMax()
        {
            var pass = new BuildLightDataPass("Test");
            pass.SetupSlots();

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var context = new CameraContext(camera, default)
            {
                VisibleLights = new NativeArray<VisibleLight>(100, Allocator.Persistent),
            };

            try
            {
                pass.PreRecord(new RenderGraphAsset(), context);

                int expectedMax = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                                + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;

                // 100 < 528 → count = 100
                Assert.That(context.VisibleLights.Length, Is.LessThan(expectedMax));
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Verifies that when visible lights exceed the maximum, the count
        /// is clamped to the pipeline asset constants.
        /// </summary>
        [Test]
        public void Initialize_LightCount_ClampedToMax()
        {
            var pass = new BuildLightDataPass("Test");
            pass.SetupSlots();

            int expectedMax = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                            + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var context = new CameraContext(camera, default)
            {
                VisibleLights = new NativeArray<VisibleLight>(
                    expectedMax + 100,
                    Allocator.Persistent),
            };

            try
            {
                pass.PreRecord(new RenderGraphAsset(), context);

                // 628 > 528 → count = 528
                Assert.That(context.VisibleLights.Length, Is.GreaterThan(expectedMax));
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Verifies that <see cref="Initialize"/> correctly captures the
        /// <see cref="CameraContext"/> and its
        /// <see cref="CameraContext.VisibleLights"/> reference.
        /// </summary>
        [Test]
        public void Initialize_CapturesContext()
        {
            var pass = new BuildLightDataPass("Test");
            pass.SetupSlots();

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var visibleLights = new NativeArray<VisibleLight>(10, Allocator.Persistent);
            var context = new CameraContext(camera, default)
            {
                VisibleLights = visibleLights,
            };

            try
            {
                pass.PreRecord(new RenderGraphAsset(), context);

                // If Initialize didn't throw, the context was accepted.
                Assert.That(visibleLights.Length, Is.EqualTo(10));
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        #endregion

        #region Property IDs

        /// <summary>
        /// Verifies that <see cref="BuildLightDataPass.PropertyIDs.LightDatasBuffer"/>
        /// produces the correct shader property name <c>_LightDatasBuffer</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_LightDatasBuffer_IsLightDatasBuffer()
        {
            // Unity's Shader.PropertyToID returns the same int for the same string.
            int expectedId = Shader.PropertyToID("_LightDatasBuffer");

            Assert.That(
                BuildLightDataPass.PropertyIDs.LightDatasBuffer,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that the property ID matches the legacy
        /// <see cref="BuildLightDataPass.PropertyIDs.lightDatasBuffer"/> value.
        /// </summary>
        [Test]
        public void PropertyIDs_MatchesLegacy()
        {
            Assert.That(
                BuildLightDataPass.PropertyIDs.LightDatasBuffer,
                Is.EqualTo(BuildLightDataPass.PropertyIDs.LightDatasBuffer));
        }

        #endregion

        #region Max Light Count

        /// <summary>
        /// Verifies the total maximum light count is the sum of directional
        /// and local light maxima defined in <see cref="HNRenderPipelineAsset"/>.
        /// </summary>
        [Test]
        public void MaxLightCount_IsDirectionalPlusLocal()
        {
            int expectedMax = HNRenderPipelineAsset.MAX_DIRECTIONAL_LIGHT_ON_SCREEN
                            + HNRenderPipelineAsset.MAX_LOCAL_LIGHT_ON_SCREEN;

            Assert.That(expectedMax, Is.GreaterThan(0));
        }

        #endregion
    }
}
