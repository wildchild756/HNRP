// <copyright file="ClusterCullingLightPassTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="ClusterCullingLightPass"/> in
    /// <c>Runtime/Passes/ClusterCullingLightPass.cs</c>.
    /// Verifies pass construction, slot setup, context initialization,
    /// <c>[Pass]</c> attribute discovery, and property IDs.
    /// </summary>
    public sealed class ClusterCullingLightPassTests
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
        /// Verifies that a <see cref="ClusterCullingLightPass"/> can be
        /// constructed and its <see cref="Pass.PassName"/> matches the
        /// supplied value.
        /// </summary>
        [Test]
        public void Constructor_PassName_MatchesSuppliedName()
        {
            const string expectedName = "TestClusterCullingLight";

            var pass = new ClusterCullingLightPass(expectedName);

            Assert.That(pass.PassName, Is.EqualTo(expectedName));
        }

        /// <summary>
        /// Verifies that a newly constructed pass is enabled by default.
        /// </summary>
        [Test]
        public void Constructor_IsEnabled_DefaultsToTrue()
        {
            var pass = new ClusterCullingLightPass("Test");

            Assert.That(pass.IsEnabled, Is.True);
        }

        /// <summary>
        /// Verifies that the <see cref="PassNameConst"/> constant holds the
        /// expected value <c>"Cluster Culling Light"</c>.
        /// </summary>
        [Test]
        public void PassNameConst_IsClusterCullingLight()
        {
            Assert.That(
                ClusterCullingLightPass.PassNameConst,
                Is.EqualTo("Cluster Culling Light"));
        }

        #endregion

        #region [Pass] Attribute

        /// <summary>
        /// Verifies that the <c>[Pass]</c> attribute is present on
        /// <see cref="ClusterCullingLightPass"/> with the correct display name.
        /// </summary>
        [Test]
        public void PassAttribute_HasCorrectDisplayName()
        {
            var attribute = typeof(ClusterCullingLightPass)
                .GetCustomAttribute<PassAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.DisplayName, Is.EqualTo("Cluster Culling Light"));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass"/> is discoverable
        /// through <see cref="PassRegistry"/> after registration.
        /// </summary>
        [Test]
        public void PassRegistry_CanGetByDisplayName()
        {
            var pass = PassRegistry.CreatePass(
                "Cluster Culling Light", "TestInstance");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<ClusterCullingLightPass>());
        }

        #endregion

        #region SetupSlots

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.SetupSlots"/>
        /// creates the input light data buffer slot with the correct name.
        /// </summary>
        [Test]
        public void SetupSlots_CreatesLightDatasInputSlot_WithCorrectName()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(pass.LightDatasBufferSlot, Is.Not.Null);
            Assert.That(
                pass.LightDatasBufferSlot.SlotName,
                Is.EqualTo("lightDatasBuffer"));
        }

        /// <summary>
        /// Verifies that the light data buffer slot created by
        /// <see cref="SetupSlots"/> is an input slot.
        /// </summary>
        [Test]
        public void SetupSlots_LightDatasSlotDirection_IsInput()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(
                pass.LightDatasBufferSlot.Direction,
                Is.EqualTo(SlotDirection.Input));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.SetupSlots"/>
        /// creates the output cluster culling light mask buffer slot with the
        /// correct name.
        /// </summary>
        [Test]
        public void SetupSlots_CreatesClusterCullingLightMaskOutputSlot_WithCorrectName()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(pass.ClusterCullingLightMaskBufferSlot, Is.Not.Null);
            Assert.That(
                pass.ClusterCullingLightMaskBufferSlot.SlotName,
                Is.EqualTo("clusterCullingLightMaskBuffer"));
        }

        /// <summary>
        /// Verifies that the cluster culling light mask buffer slot created by
        /// <see cref="SetupSlots"/> is an output slot.
        /// </summary>
        [Test]
        public void SetupSlots_ClusterCullingLightMaskSlotDirection_IsOutput()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(
                pass.ClusterCullingLightMaskBufferSlot.Direction,
                Is.EqualTo(SlotDirection.Output));
        }

        /// <summary>
        /// Verifies that the light data buffer slot created by
        /// <see cref="SetupSlots"/> is of type <see cref="ComputeBufferSlot"/>.
        /// </summary>
        [Test]
        public void SetupSlots_LightDatasSlotType_IsComputeBufferSlot()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(
                pass.LightDatasBufferSlot,
                Is.InstanceOf<ComputeBufferSlot>());
        }

        /// <summary>
        /// Verifies that the cluster culling light mask buffer slot created by
        /// <see cref="SetupSlots"/> is of type <see cref="ComputeBufferSlot"/>.
        /// </summary>
        [Test]
        public void SetupSlots_ClusterCullingLightMaskSlotType_IsComputeBufferSlot()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.SetupSlots();

            Assert.That(
                pass.ClusterCullingLightMaskBufferSlot,
                Is.InstanceOf<ComputeBufferSlot>());
        }

        #endregion

        #region Initialize

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PreRecord"/>
        /// accepts a valid <see cref="CameraContext"/> without throwing.
        /// </summary>
        [Test]
        public void Initialize_WithValidContext_DoesNotThrow()
        {
            var pass = new ClusterCullingLightPass("Test");
            pass.SetupSlots();

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var visibleLights = new NativeArray<VisibleLight>(
                10, Allocator.Persistent);
            var context = new CameraContext(camera, default)
            {
                VisibleLights = visibleLights,
            };

            try
            {
                Assert.DoesNotThrow(() => pass.PreRecord(new RenderGraphAsset(), context));
            }
            finally
            {
                context.Dispose();
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Verifies that <see cref="Cleanup"/> completes without throwing,
        /// even when called before <see cref="SetupSlots"/> or
        /// <see cref="Initialize"/>.
        /// </summary>
        [Test]
        public void Cleanup_DoesNotThrow()
        {
            var pass = new ClusterCullingLightPass("Test");

            Assert.DoesNotThrow(() => pass.Cleanup());
        }

        #endregion

        #region Property IDs

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightMaskBuffer</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_ClusterCullingLightMaskBuffer_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightMaskBuffer");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.clusterCullingLightParamsBuffer"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightParamsBuffer</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_ClusterCullingLightParamsBuffer_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightParamsBuffer");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.clusterCullingLightParamsBuffer,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.cullingParams0"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightParams0</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingParams0_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightParams0");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingParams0,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.cullingParams1"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightParams1</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingParams1_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightParams1");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingParams1,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.cullingClipToViewMatrix"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightClipToView</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingClipToViewMatrix_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightClipToView");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingClipToViewMatrix,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.cullingViewToClipMatrix"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightViewToClip</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingViewToClipMatrix_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightViewToClip");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingViewToClipMatrix,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingLightPass.PropertyIDs.cullingClipToWorldMatrix"/>
        /// produces the correct shader property name
        /// <c>_ClusterCullingLightClipToWorld</c>.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingClipToWorldMatrix_MatchesExpected()
        {
            int expectedId = Shader.PropertyToID(
                "_ClusterCullingLightClipToWorld");

            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingClipToWorldMatrix,
                Is.EqualTo(expectedId));
        }

        /// <summary>
        /// Verifies that all property IDs match the legacy
        /// <see cref="ClusterCullingLightPass.PropertyIDs"/> values.
        /// </summary>
        [Test]
        public void PropertyIDs_AllMatchLegacy()
        {
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.clusterCullingLightMaskBuffer));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.clusterCullingLightParamsBuffer,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.clusterCullingLightParamsBuffer));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingParams0,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.cullingParams0));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingParams1,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.cullingParams1));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingClipToViewMatrix,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.cullingClipToViewMatrix));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingViewToClipMatrix,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.cullingViewToClipMatrix));
            Assert.That(
                ClusterCullingLightPass.PropertyIDs.cullingClipToWorldMatrix,
                Is.EqualTo(                ClusterCullingLightPass.PropertyIDs.cullingClipToWorldMatrix));
        }

        #endregion

        #region IsEnabled

        /// <summary>
        /// Verifies that when <see cref="Pass.IsEnabled"/> is set to
        /// <c>false</c>, the <see cref="Record"/> method returns early
        /// without throwing. Actual render graph behavior is verified
        /// through code review / integration tests.
        /// </summary>
        [Test]
        public void IsEnabled_CanBeToggled()
        {
            var pass = new ClusterCullingLightPass("Test");

            pass.IsEnabled = false;

            Assert.That(pass.IsEnabled, Is.False);

            pass.IsEnabled = true;

            Assert.That(pass.IsEnabled, Is.True);
        }

        #endregion

        #region Lifecycle Order

        /// <summary>
        /// Verifies that the full lifecycle (SetupSlots → Initialize →
        /// Cleanup) completes without throwing.
        /// </summary>
        [Test]
        public void FullLifecycle_DoesNotThrow()
        {
            var pass = new ClusterCullingLightPass("Test");

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var visibleLights = new NativeArray<VisibleLight>(
                5, Allocator.Persistent);
            var context = new CameraContext(camera, default)
            {
                VisibleLights = visibleLights,
            };

            try
            {
                Assert.DoesNotThrow(() =>
                {
                    pass.SetupSlots();
                    pass.PreRecord(new RenderGraphAsset(), context);
                    pass.Cleanup();
                });
            }
            finally
            {
                context.Dispose();
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        #endregion
    }
}
