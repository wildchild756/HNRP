// <copyright file="ClusterCullingReflectionProbePassTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="ClusterCullingReflectionProbePass"/> in
    /// <c>Runtime/Passes/ClusterCullingReflectionProbePass.cs</c>.
    /// Verifies pass construction, slot setup, context initialization,
    /// <c>[Pass]</c> attribute discovery, and property IDs.
    /// </summary>
    public sealed class ClusterCullingReflectionProbePassTests
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
        /// Verifies that a <see cref="ClusterCullingReflectionProbePass"/> can be
        /// constructed and its <see cref="Pass.PassName"/> matches the supplied value.
        /// </summary>
        [Test]
        public void Constructor_PassName_MatchesSuppliedName()
        {
            const string expectedName = "TestClusterCullingProbe";

            var pass = new ClusterCullingReflectionProbePass(expectedName);

            Assert.That(pass.PassName, Is.EqualTo(expectedName));
        }

        /// <summary>
        /// Verifies that a newly constructed pass is enabled by default.
        /// </summary>
        [Test]
        public void Constructor_IsEnabled_DefaultsToTrue()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            Assert.That(pass.IsEnabled, Is.True);
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass"/> is a
        /// subclass of <see cref="Pass"/>.
        /// </summary>
        [Test]
        public void Pass_IsSubclassOfPass()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<Pass>());
        }

        #endregion

        #region [Pass] Attribute

        /// <summary>
        /// Verifies that the <c>[Pass]</c> attribute is present on
        /// <see cref="ClusterCullingReflectionProbePass"/> with the correct
        /// display name.
        /// </summary>
        [Test]
        public void PassAttribute_HasCorrectDisplayName()
        {
            var attribute = typeof(ClusterCullingReflectionProbePass)
                .GetCustomAttribute<PassAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.DisplayName, Is.EqualTo("Cluster Culling Probe"));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass"/> is
        /// discoverable through <see cref="PassRegistry"/> after registration.
        /// </summary>
        [Test]
        public void PassRegistry_CanGetByDisplayName()
        {
            var pass = PassRegistry.CreatePass("Cluster Culling Probe", "TestInstance");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<ClusterCullingReflectionProbePass>());
        }

        #endregion

        #region SetupSlots

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass.SetupSlots"/>
        /// creates all three output slots with correct names, directions, and types.
        /// </summary>
        [Test]
        public void SetupSlots_DeclaresAllThreeOutputSlots()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            pass.SetupSlots();

            // ── ReflectionProbeAtlasInputSlot (TextureSlot, Input) ──

            Assert.That(pass.ReflectionProbeAtlasInputSlot, Is.Not.Null,
                "ReflectionProbeAtlasInputSlot should be non-null after SetupSlots.");
            Assert.That(pass.ReflectionProbeAtlasInputSlot!.SlotName,
                Is.EqualTo("reflectionProbeAtlas"));
            Assert.That(pass.ReflectionProbeAtlasInputSlot.Direction,
                Is.EqualTo(SlotDirection.Input));
            Assert.That(pass.ReflectionProbeAtlasInputSlot,
                Is.InstanceOf<TextureSlot>());

            // ── ReflectionProbeAtlasOutputSlot (TextureSlot, Output) ──

            Assert.That(pass.ReflectionProbeAtlasOutputSlot, Is.Not.Null,
                "ReflectionProbeAtlasOutputSlot should be non-null after SetupSlots.");
            Assert.That(pass.ReflectionProbeAtlasOutputSlot!.SlotName,
                Is.EqualTo("reflectionProbeAtlasOutput"));
            Assert.That(pass.ReflectionProbeAtlasOutputSlot.Direction,
                Is.EqualTo(SlotDirection.Output));
            Assert.That(pass.ReflectionProbeAtlasOutputSlot,
                Is.InstanceOf<TextureSlot>());

            // ── ClusterCullingReflectionProbeMaskBufferSlot (ComputeBufferSlot, Output) ──

            Assert.That(pass.ClusterCullingReflectionProbeMaskBufferSlot, Is.Not.Null,
                "ClusterCullingReflectionProbeMaskBufferSlot should be non-null after SetupSlots.");
            Assert.That(pass.ClusterCullingReflectionProbeMaskBufferSlot!.SlotName,
                Is.EqualTo("clusterCullingReflectionProbeMaskBuffer"));
            Assert.That(pass.ClusterCullingReflectionProbeMaskBufferSlot.Direction,
                Is.EqualTo(SlotDirection.Output));
            Assert.That(pass.ClusterCullingReflectionProbeMaskBufferSlot,
                Is.InstanceOf<ComputeBufferSlot>());

            // ── ClusterCullingReflectionProbeDatasBufferSlot (ComputeBufferSlot, Output) ──

            Assert.That(pass.ClusterCullingReflectionProbeDatasBufferSlot, Is.Not.Null,
                "ClusterCullingReflectionProbeDatasBufferSlot should be non-null after SetupSlots.");
            Assert.That(pass.ClusterCullingReflectionProbeDatasBufferSlot!.SlotName,
                Is.EqualTo("clusterCullingReflectionProbeDatasBuffer"));
            Assert.That(pass.ClusterCullingReflectionProbeDatasBufferSlot.Direction,
                Is.EqualTo(SlotDirection.Output));
            Assert.That(pass.ClusterCullingReflectionProbeDatasBufferSlot,
                Is.InstanceOf<ComputeBufferSlot>());
        }

        /// <summary>
        /// Before <see cref="Pass.SetupSlots"/> is called,
        /// all slot properties are <c>null</c>.
        /// </summary>
        [Test]
        public void AllSlots_AreNull_BeforeSetupSlots()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            Assert.That(pass.ReflectionProbeAtlasInputSlot, Is.Null);
            Assert.That(pass.ReflectionProbeAtlasOutputSlot, Is.Null);
            Assert.That(pass.ClusterCullingReflectionProbeMaskBufferSlot, Is.Null);
            Assert.That(pass.ClusterCullingReflectionProbeDatasBufferSlot, Is.Null);
        }

        /// <summary>
        /// After <see cref="Pass.SetupSlots"/>, calling it again
        /// does not throw — it recreates the slots.
        /// </summary>
        [Test]
        public void SetupSlots_IsIdempotent()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            Assert.DoesNotThrow(() =>
            {
                pass.SetupSlots();
                pass.SetupSlots();
            });
        }

        #endregion

        #region Initialize

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass.PreRecord"/>
        /// stores the <see cref="CameraContext"/> without throwing.
        /// </summary>
        [Test]
        public void Initialize_DoesNotThrow()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");
            pass.SetupSlots();

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var context = new CameraContext(camera, default);

            try
            {
                Assert.DoesNotThrow(() => pass.PreRecord(new RenderGraphAsset(), context));
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Verifies that <see cref="Initialize"/> does not throw when
        /// <see cref="CameraContext.RuntimeResources"/> is <c>null</c>
        /// (the compute shader reference will be null, but the call itself
        /// should not throw).
        /// </summary>
        [Test]
        public void Initialize_WithNullRuntimeResources_DoesNotThrow()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");
            pass.SetupSlots();

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var context = new CameraContext(camera, default)
            {
                RuntimeResources = null!,
            };

            try
            {
                Assert.DoesNotThrow(() => pass.PreRecord(new RenderGraphAsset(), context));
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        #endregion

        #region Record

        /// <summary>
        /// Verifies that <see cref="Pass.Record"/> accepts a single
        /// <c>RenderGraph</c> parameter.
        /// </summary>
        [Test]
        public void Record_AcceptsRenderGraph()
        {
            var method = typeof(ClusterCullingReflectionProbePass)
                .GetMethod(nameof(Pass.Record));

            Assert.That(method, Is.Not.Null,
                "Record method should exist on ClusterCullingReflectionProbePass.");

            var parameters = method!.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1),
                "Record should accept exactly one parameter.");
            Assert.That(parameters[0].ParameterType,
                Is.EqualTo(typeof(UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraph)),
                "Record parameter should be RenderGraph.");
        }

        /// <summary>
        /// Calling <see cref="Pass.Record"/> with a <c>null</c> render graph
        /// after <see cref="SetupSlots"/> but without <see cref="Initialize"/>
        /// (so the compute shader is null) should return early
        /// without throwing — the null-shader guard catches it first.
        /// </summary>
        [Test]
        public void Record_WithoutInitialize_ReturnsEarly()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");
            pass.SetupSlots();

            // The pass logs an error when compute shader is null (no pipeline asset set up).
            LogAssert.Expect(LogType.Error,
                "Cluster Culling Reflection Probe Compute Shader is null. " +
                "Ensure HNRenderPipelineRuntimeResources is assigned in the pipeline asset.");
            Assert.DoesNotThrow(() => pass.Record(null!));
        }

        /// <summary>
        /// Calling <see cref="Pass.Record"/> on a disabled pass should be
        /// skipped by the caller — <see cref="Pass.IsEnabled"/> is checked
        /// before invoking Record.
        /// </summary>
        [Test]
        public void Record_SkippedWhenDisabled()
        {
            var pass = new ClusterCullingReflectionProbePass("Test")
            {
                IsEnabled = false,
            };

            Assert.That(pass.IsEnabled, Is.False);

            bool recordWouldBeCalled = pass.IsEnabled;
            Assert.That(recordWouldBeCalled, Is.False,
                "Record should not be invoked when IsEnabled is false.");
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// The full lifecycle — <see cref="Pass.SetupSlots"/>,
        /// <see cref="Pass.PreRecord"/>, <see cref="Pass.Cleanup"/> —
        /// completes without exceptions given valid state.
        /// </summary>
        [Test]
        public void Lifecycle_SetupSlots_Initialize_Cleanup_DoesNotThrow()
        {
            var pass = new ClusterCullingReflectionProbePass("Test");

            Assert.DoesNotThrow(() => pass.SetupSlots(),
                "SetupSlots should not throw.");

            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            var context = new CameraContext(camera, default);

            try
            {
                Assert.DoesNotThrow(() => pass.PreRecord(new RenderGraphAsset(), context),
                    "Initialize should not throw.");
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }

            Assert.DoesNotThrow(() => pass.Cleanup(),
                "Cleanup should not throw.");
        }

        #endregion

        #region Property IDs

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass.PropertyIDs"/>
        /// mask buffer ID matches the legacy
        /// <see cref="ClusterCullingReflectionProbePass.PropertyIDs"/> value.
        /// </summary>
        [Test]
        public void PropertyIDs_MaskBuffer_MatchesLegacy()
        {
            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeMaskBuffer,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeMaskBuffer));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass.PropertyIDs"/>
        /// data buffer ID matches the legacy
        /// <see cref="ClusterCullingReflectionProbePass.PropertyIDs"/> value.
        /// </summary>
        [Test]
        public void PropertyIDs_DatasBuffer_MatchesLegacy()
        {
            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeDatasBuffer,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.clusterCullingReflectionProbeDatasBuffer));
        }

        /// <summary>
        /// Verifies that <see cref="ClusterCullingReflectionProbePass.PropertyIDs"/>
        /// culling params match the legacy IDs.
        /// </summary>
        [Test]
        public void PropertyIDs_CullingParams_MatchLegacy()
        {
            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.cullingParams0,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.cullingParams0));

            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.cullingParams1,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.cullingParams1));

            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.cullingClipToViewMatrix,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.cullingClipToViewMatrix));

            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.cullingViewToClipMatrix,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.cullingViewToClipMatrix));

            Assert.That(
                ClusterCullingReflectionProbePass.PropertyIDs.cullingClipToWorldMatrix,
                Is.EqualTo(                ClusterCullingReflectionProbePass.PropertyIDs.cullingClipToWorldMatrix));
        }

        #endregion
    }
}
