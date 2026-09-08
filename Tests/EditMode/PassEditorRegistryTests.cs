// <copyright file="PassEditorRegistryTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using NUnit.Framework;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using HN.HNRP;
using HN.HNRP.Editor;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="PassEditorRegistry"/> — verifies that pass types map
    /// to their bound editors via <see cref="PassEditorAttribute"/>, and that
    /// unknown pass types fall back to the default editor.
    /// </summary>
    public sealed class PassEditorRegistryTests
    {
        /// <summary>
        /// A pass type without a dedicated editor binding.
        /// </summary>
        [Pass("UnboundPass")]
        private sealed class UnboundPass : Pass
        {
            public UnboundPass()
            {
            }

            public UnboundPass(string name)
                : base(name)
            {
            }

            /// <inheritdoc />
            public override void SetupSlots()
            {
            }

            /// <inheritdoc />
            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
            }

            /// <inheritdoc />
            public override void Record(RenderGraph renderGraph)
            {
            }

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        [Test]
        public void GetEditor_ReturnsBoundEditor_ForKnownPassType()
        {
            PassEditor editor = PassEditorRegistry.GetEditor(typeof(DrawObjectPass));

            Assert.That(editor, Is.Not.Null,
                "GetEditor should return a non-null editor.");
            Assert.That(editor, Is.InstanceOf<DrawObjectPassEditor>(),
                "DrawObjectPass should map to DrawObjectPassEditor.");
        }

        [Test]
        public void GetEditor_ReturnsBoundEditor_ForRenderOutputPass()
        {
            PassEditor editor = PassEditorRegistry.GetEditor(typeof(RenderOutputPass));

            Assert.That(editor, Is.Not.Null,
                "GetEditor should return a non-null editor.");
            Assert.That(editor, Is.InstanceOf<RenderOutputPassEditor>(),
                "RenderOutputPass should map to RenderOutputPassEditor.");
        }

        [Test]
        public void GetEditor_ReturnsDefault_ForUnboundPassType()
        {
            PassEditor editor = PassEditorRegistry.GetEditor(typeof(UnboundPass));

            Assert.That(editor, Is.Not.Null,
                "GetEditor should never return null for a valid pass type.");
            Assert.That(editor, Is.InstanceOf<DefaultPassEditor>(),
                "Unbound pass types should fall back to the default editor.");
        }

        [Test]
        public void GetEditor_NullType_ReturnsDefault()
        {
            PassEditor editor = PassEditorRegistry.GetEditor(null);

            Assert.That(editor, Is.Not.Null,
                "GetEditor(null) should return the default editor.");
            Assert.That(editor, Is.InstanceOf<DefaultPassEditor>(),
                "Null type should fall back to the default editor.");
        }

        [Test]
        public void BoundEditor_DefinesPresets()
        {
            PassEditor editor = PassEditorRegistry.GetEditor(typeof(DrawObjectPass));

            Assert.That(editor.Presets, Is.Not.Null,
                "DrawObjectPassEditor should expose a preset list.");
            Assert.That(editor.Presets.Count, Is.GreaterThan(0),
                "DrawObjectPassEditor should define at least one preset.");
        }
    }
}
