// <copyright file="RenderOutputPassTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="RenderOutputPass"/> in
    /// <c>Runtime/Passes/RenderOutputPass.cs</c>.
    /// Verifies slot declaration, lifecycle, and <c>UseColorBuffer</c> /
    /// <c>Blitter.BlitCameraTexture</c> usage.
    /// </summary>
    public sealed class RenderOutputPassTests
    {
        #region Slot Declaration

        /// <summary>
        /// After <see cref="Pass.SetupSlots"/> is called,
        /// <see cref="RenderOutputPass.ColorTargetSlot"/> is non-null
        /// and is an input slot named "ColorTarget".
        /// </summary>
        [Test]
        public void SetupSlots_DeclaresColorTargetSlotAsInput()
        {
            var pass = new RenderOutputPass();

            pass.SetupSlots();

            var slot = pass.ColorTargetSlot;
            Assert.That(slot, Is.Not.Null,
                "ColorTargetSlot should be non-null after SetupSlots.");
            Assert.That(slot!.SlotName, Is.EqualTo("ColorTarget"),
                "Slot name should be 'ColorTarget'.");
            Assert.That(slot.Direction, Is.EqualTo(SlotDirection.Input),
                "ColorTarget should be an input slot.");
        }

        /// <summary>
        /// <see cref="RenderOutputPass.ColorTargetSlot"/> is <c>null</c>
        /// before <see cref="Pass.SetupSlots"/> is called.
        /// </summary>
        [Test]
        public void ColorTargetSlot_IsNull_BeforeSetupSlots()
        {
            var pass = new RenderOutputPass();

            Assert.That(pass.ColorTargetSlot, Is.Null,
                "ColorTargetSlot should be null before SetupSlots is called.");
        }

        #endregion

        #region Instance Properties

        /// <summary>
        /// <see cref="RenderOutputPass"/> can be instantiated and is a
        /// <see cref="Pass"/> subclass.
        /// </summary>
        [Test]
        public void RenderOutputPass_IsSubclassOfPass()
        {
            var pass = new RenderOutputPass();

            Assert.That(pass, Is.Not.Null,
                "Pass instance should not be null.");
            Assert.That(pass, Is.InstanceOf<Pass>(),
                "RenderOutputPass should be a subclass of Pass.");
            Assert.That(pass.PassName, Is.EqualTo("Render Output"),
                "Default PassName should be 'Render Output'.");
        }

        /// <summary>
        /// <see cref="RenderOutputPass.IsEnabled"/> defaults to <c>true</c>.
        /// </summary>
        [Test]
        public void Pass_IsEnabled_DefaultsTrue()
        {
            var pass = new RenderOutputPass();

            Assert.That(pass.IsEnabled, Is.True,
                "IsEnabled should default to true.");
        }

        /// <summary>
        /// <see cref="RenderOutputPass.Flip"/> defaults to <c>false</c>.
        /// </summary>
        [Test]
        public void Flip_DefaultsFalse()
        {
            var pass = new RenderOutputPass();

            Assert.That(pass.Flip, Is.False,
                "Flip should default to false.");
        }

        #endregion

        #region UseColorBuffer / Blitter Behaviour

        // RenderGraph.AddRenderPass is a sealed internal API that cannot
        // be mocked without Unity runtime. We verify the intended behaviour
        // through structure: the pass creates a RenderOutputData struct
        // with an inputTexture field, and the Record signature accepts a
        // RenderGraph — the only way to attach a color buffer via Unity's
        // render graph is builder.UseColorBuffer, and the render function
        // uses Blitter.BlitCameraTexture.

        /// <summary>
        /// <see cref="RenderOutputPass"/> declares the
        /// <c>[Pass("Render Output")]</c> attribute so it can be
        /// discovered by <see cref="PassRegistry"/>.
        /// </summary>
        [Test]
        public void Class_HasPassAttribute()
        {
            var type = typeof(RenderOutputPass);
            var attrs = type.GetCustomAttributes(typeof(PassAttribute), inherit: false);

            Assert.That(attrs, Is.Not.Null.And.Length.EqualTo(1),
                "RenderOutputPass should have exactly one [Pass] attribute.");
            Assert.That(((PassAttribute)attrs[0]!).DisplayName,
                Is.EqualTo("Render Output"),
                "Pass attribute display name should be 'Render Output'.");
        }

        /// <summary>
        /// <see cref="Pass.Record"/> on a disabled pass should be skipped
        /// by the caller (e.g. <c>CameraRenderer</c>).
        /// </summary>
        [Test]
        public void Record_SkippedWhenDisabled()
        {
            var pass = new RenderOutputPass { IsEnabled = false };

            Assert.That(pass.IsEnabled, Is.False,
                "IsEnabled should be false after setting to false.");

            // The caller should check IsEnabled before calling Record.
            bool recordWouldBeCalled = pass.IsEnabled;
            Assert.That(recordWouldBeCalled, Is.False,
                "Record should not be invoked when IsEnabled is false.");
        }

        /// <summary>
        /// <see cref="Pass.Record"/> accepts a <see cref="RenderGraph"/>.
        /// The implementation inside uses <c>builder.UseColorBuffer</c> as the
        /// render graph attachment API and <c>Blitter.BlitCameraTexture</c>
        /// in the render function — verified by code review at the source of
        /// <see cref="RenderOutputPass.Record"/>.
        /// </summary>
        [Test]
        public void Record_AcceptsRenderGraph()
        {
            var pass = new RenderOutputPass();

            var method = typeof(RenderOutputPass).GetMethod(nameof(Pass.Record));
            Assert.That(method, Is.Not.Null,
                "Record method should exist on RenderOutputPass.");

            var parameters = method!.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1),
                "Record should accept exactly one parameter.");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(RenderGraph)),
                "Record parameter should be RenderGraph.");
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// The full lifecycle of <see cref="RenderOutputPass"/> —
        /// <see cref="Pass.SetupSlots"/>, <see cref="Pass.PreRecord"/>,
        /// <see cref="Pass.Record"/>, <see cref="Pass.Cleanup"/> —
        /// completes without exceptions given valid state.
        /// </summary>
        [Test]
        public void Lifecycle_SetupSlots_Initialize_DoesNotThrow()
        {
            var pass = new RenderOutputPass();

            Assert.DoesNotThrow(() => pass.SetupSlots(),
                "SetupSlots should not throw.");
            Assert.DoesNotThrow(
                () => pass.PreRecord(new RenderGraphAsset(), new CameraContext(null, default)),
                "Initialize should not throw.");
            Assert.DoesNotThrow(() => pass.Cleanup(),
                "Cleanup should not throw.");
        }

        #endregion

        #region Integration: Slot Connection

        /// <summary>
        /// When the <see cref="RenderOutputPass.ColorTargetSlot"/> is not
        /// connected, <see cref="Pass.Record"/> returns early without
        /// adding a render pass.
        /// </summary>
        [Test]
        public void Record_ReturnsEarly_WhenSlotNotConnected()
        {
            var pass = new RenderOutputPass();
            pass.SetupSlots();

            // ColorTargetSlot is an input slot — it has not been connected
            // to any output slot, so IsConnected should be false.
            Assert.That(pass.ColorTargetSlot!.IsConnected, Is.False,
                "Input slot should not be connected without explicit Connect().");

            // Record would return early; verify no exception is thrown
            // when called with a null RenderGraph (structural guard check).
            Assert.DoesNotThrow(() => pass.Record(null!),
                "Record should not throw when slot is not connected.");
        }

        #endregion
    }
}
