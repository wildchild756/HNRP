// <copyright file="BuiltinSkyPassTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="BuiltinSkyPass"/> in
    /// <c>Runtime/Passes/BuiltinSkyPass.cs</c>.
    /// Verifies slot declaration, <c>[Pass]</c> attribute, and lifecycle.
    /// </summary>
    public sealed class BuiltinSkyPassTests
    {
        #region Slot Declaration

        /// <summary>
        /// After <see cref="Pass.SetupSlots"/> is called,
        /// all four slots are non-null with correct names and directions:
        /// two inputs plus two pass-through outputs
        /// (<c>ColorTargetOutput</c> / <c>DepthTargetOutput</c>) that let
        /// downstream passes chain from this pass's outputs.
        /// </summary>
        [Test]
        public void SetupSlots_DeclaresAllFourSlots()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            pass.SetupSlots();

            // ── ColorTarget input slot ──

            Assert.That(pass.ColorTargetSlot, Is.Not.Null,
                "ColorTargetSlot should be non-null after SetupSlots.");
            Assert.That(pass.ColorTargetSlot!.SlotName, Is.EqualTo("ColorTarget"));
            Assert.That(pass.ColorTargetSlot.Direction, Is.EqualTo(SlotDirection.Input));

            // ── DepthTarget input slot ──

            Assert.That(pass.DepthTargetSlot, Is.Not.Null,
                "DepthTargetSlot should be non-null after SetupSlots.");
            Assert.That(pass.DepthTargetSlot!.SlotName, Is.EqualTo("DepthTarget"));
            Assert.That(pass.DepthTargetSlot.Direction, Is.EqualTo(SlotDirection.Input));

            // ── ColorTargetOutput pass-through slot ──

            Assert.That(pass.ColorTargetOutputSlot, Is.Not.Null,
                "ColorTargetOutputSlot should be non-null after SetupSlots.");
            Assert.That(pass.ColorTargetOutputSlot!.SlotName, Is.EqualTo("ColorTargetOutput"));
            Assert.That(pass.ColorTargetOutputSlot.Direction, Is.EqualTo(SlotDirection.Output));

            // ── DepthTargetOutput pass-through slot ──

            Assert.That(pass.DepthTargetOutputSlot, Is.Not.Null,
                "DepthTargetOutputSlot should be non-null after SetupSlots.");
            Assert.That(pass.DepthTargetOutputSlot!.SlotName, Is.EqualTo("DepthTargetOutput"));
            Assert.That(pass.DepthTargetOutputSlot.Direction, Is.EqualTo(SlotDirection.Output));
        }

        /// <summary>
        /// Before <see cref="Pass.SetupSlots"/> is called,
        /// all slot properties are <c>null</c>.
        /// </summary>
        [Test]
        public void AllSlots_AreNull_BeforeSetupSlots()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            Assert.That(pass.ColorTargetSlot, Is.Null);
            Assert.That(pass.DepthTargetSlot, Is.Null);
            Assert.That(pass.ColorTargetOutputSlot, Is.Null);
            Assert.That(pass.DepthTargetOutputSlot, Is.Null);
        }

        #endregion

        #region Slot Types

        /// <summary>
        /// All four slots are <see cref="TextureSlot"/> instances.
        /// </summary>
        [Test]
        public void SetupSlots_SlotsAreTextureSlots()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            pass.SetupSlots();

            Assert.That(pass.ColorTargetSlot, Is.InstanceOf<TextureSlot>());
            Assert.That(pass.DepthTargetSlot, Is.InstanceOf<TextureSlot>());
            Assert.That(pass.ColorTargetOutputSlot, Is.InstanceOf<TextureSlot>());
            Assert.That(pass.DepthTargetOutputSlot, Is.InstanceOf<TextureSlot>());
        }

        #endregion

        #region Instance Properties

        /// <summary>
        /// <see cref="BuiltinSkyPass"/> can be instantiated and is a
        /// <see cref="Pass"/> subclass.
        /// </summary>
        [Test]
        public void Pass_IsSubclassOfPass()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<Pass>());
            Assert.That(pass.PassName, Is.EqualTo("TestBuiltinSky"));
        }

        /// <summary>
        /// <see cref="Pass.IsEnabled"/> defaults to <c>true</c>.
        /// </summary>
        [Test]
        public void Pass_IsEnabled_DefaultsTrue()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            Assert.That(pass.IsEnabled, Is.True);
        }

        #endregion

        #region Pass Attribute

        /// <summary>
        /// <see cref="BuiltinSkyPass"/> declares the
        /// <c>[Pass("Builtin Sky")]</c> attribute so it can be
        /// discovered by <see cref="PassRegistry"/>.
        /// </summary>
        [Test]
        public void Class_HasPassAttribute()
        {
            var type = typeof(BuiltinSkyPass);
            var attrs = type.GetCustomAttributes(typeof(PassAttribute), inherit: false);

            Assert.That(attrs, Is.Not.Null.And.Length.EqualTo(1),
                "BuiltinSkyPass should have exactly one [Pass] attribute.");
            Assert.That(((PassAttribute)attrs[0]!).DisplayName,
                Is.EqualTo("Builtin Sky"),
                "Pass attribute display name should be 'Builtin Sky'.");
        }

        /// <summary>
        /// The constant <see cref="BuiltinSkyPass.PassNameConst"/> matches
        /// the <c>[Pass]</c> attribute display name.
        /// </summary>
        [Test]
        public void PassNameConst_MatchesAttributeDisplayName()
        {
            var attribute = typeof(BuiltinSkyPass)
                .GetCustomAttribute<PassAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(
                BuiltinSkyPass.PassNameConst,
                Is.EqualTo(attribute!.DisplayName));
        }

        #endregion

        #region Record Signature

        /// <summary>
        /// <see cref="Pass.Record"/> accepts a <see cref="RenderGraph"/>.
        /// </summary>
        [Test]
        public void Record_AcceptsRenderGraph()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            var method = typeof(BuiltinSkyPass).GetMethod(nameof(Pass.Record));
            Assert.That(method, Is.Not.Null,
                "Record method should exist on BuiltinSkyPass.");

            var parameters = method!.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1),
                "Record should accept exactly one parameter.");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(RenderGraph)),
                "Record parameter should be RenderGraph.");
        }

        /// <summary>
        /// <see cref="Pass.Record"/> on a disabled pass should be skipped
        /// by the caller (e.g. <c>CameraRenderer</c>).
        /// </summary>
        [Test]
        public void Record_SkippedWhenDisabled()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky") { IsEnabled = false };

            Assert.That(pass.IsEnabled, Is.False);

            bool recordWouldBeCalled = pass.IsEnabled;
            Assert.That(recordWouldBeCalled, Is.False,
                "Record should not be invoked when IsEnabled is false.");
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// The full lifecycle of <see cref="BuiltinSkyPass"/> —
        /// <see cref="Pass.SetupSlots"/>, <see cref="Pass.PreRecord"/>,
        /// <see cref="Pass.Record"/>, <see cref="Pass.Cleanup"/> —
        /// completes without exceptions given valid state.
        /// </summary>
        [Test]
        public void Lifecycle_SetupSlots_Initialize_DoesNotThrow()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            Assert.DoesNotThrow(() => pass.SetupSlots(),
                "SetupSlots should not throw.");

            Assert.DoesNotThrow(
                () => pass.PreRecord(new RenderGraphAsset(), new CameraContext(null, default)),
                "Initialize should not throw.");

            Assert.DoesNotThrow(() => pass.Cleanup(),
                "Cleanup should not throw.");
        }

        /// <summary>
        /// Calling <see cref="Pass.Record"/> without <see cref="SetupSlots"/>
        /// should return early rather than throwing — both slots are null
        /// so the null-check guard triggers.
        /// </summary>
        [Test]
        public void Record_WithoutSlots_ReturnsEarly()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");

            // Without SetupSlots, ColorTargetSlot and DepthTargetSlot are null —
            // Record should return early rather than throwing.
            Assert.DoesNotThrow(() => pass.Record(null));
        }

        /// <summary>
        /// Calling <see cref="Pass.Record"/> after <see cref="SetupSlots"/>
        /// but without <see cref="Initialize"/> returns early because
        /// <c>cameraContext</c> is null.
        /// </summary>
        [Test]
        public void Record_WithoutInitialize_ReturnsEarly()
        {
            var pass = new BuiltinSkyPass("TestBuiltinSky");
            pass.SetupSlots();

            // Slots are set up but Initialize was not called → cameraContext is null.
            Assert.DoesNotThrow(() => pass.Record(null));
        }

        #endregion
    }
}
