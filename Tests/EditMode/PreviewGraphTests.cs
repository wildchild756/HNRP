// <copyright file="PreviewGraphTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HN.HNRP;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <c>PreviewGraph.asset</c> — the lightweight render graph
    /// used for preview cameras. Verifies that the asset exists, builds the
    /// minimal pass set (draw object + render output), omits heavy passes,
    /// materializes its resource nodes, and has preview-oriented settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PreviewGraph is designed to be a stripped-down version of StandardGraph.
    /// It includes only the minimum passes needed for basic opaque rendering:
    /// an <c>opaque</c> <see cref="DrawObjectPass"/> and a <c>finalBlit</c>
    /// render output pass, wired through ColorBuffer / DepthBuffer /
    /// OpaqueRendererList resource nodes.
    /// </para>
    /// <para>
    /// Passes intentionally <b>excluded</b> from PreviewGraph:
    /// <list type="bullet">
    ///   <item>Build Light Data — lighting data collection is skipped</item>
    ///   <item>Cluster Culling Light — light culling is skipped</item>
    ///   <item>Cluster Culling Probe — reflection probe culling is skipped</item>
    ///   <item>Builtin Sky — skybox rendering is skipped</item>
    ///   <item>Transparency — transparent object rendering is skipped</item>
    ///   <item>Editor Wire Overlay — editor debug overlay is skipped</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class PreviewGraphTests
    {
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

        #region Asset Existence & Type

        /// <summary>
        /// PreviewGraph.asset exists in Resources and is a valid
        /// <see cref="RenderGraphAsset"/>.
        /// </summary>
        [Test]
        public void PreviewGraph_CanBeLoaded_FromResources()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");

            Assert.That(asset, Is.Not.Null,
                "PreviewGraph.asset should exist at Runtime/Resources/RenderGraphs/PreviewGraph.asset.");
            Assert.That(asset, Is.InstanceOf<RenderGraphAsset>(),
                "Loaded asset should be a RenderGraphAsset.");
        }

        #endregion

        #region Pass Composition

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> succeeds on the real asset and
        /// produces exactly the minimal preview pass set: the <c>opaque</c>
        /// draw pass and the <c>finalBlit</c> render output pass, in
        /// topological (producer-before-consumer) order.
        /// </summary>
        [Test]
        public void PreviewGraph_HasExpectedPasses()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "PreviewGraph should build exactly 2 passes.");

            Assert.That(result.Exists(p => p.PassName == "opaque"), Is.True,
                "PreviewGraph should contain an 'opaque' draw pass.");
            Assert.That(result.Exists(p => p.PassName == "finalBlit"), Is.True,
                "PreviewGraph should contain a 'finalBlit' render output pass.");

            int opaqueIndex = result.FindIndex(p => p.PassName == "opaque");
            int finalBlitIndex = result.FindIndex(p => p.PassName == "finalBlit");
            Assert.That(opaqueIndex, Is.LessThan(finalBlitIndex),
                "opaque (color producer) must be ordered before finalBlit.");
        }

        /// <summary>
        /// After <see cref="RenderGraphAsset.Build"/>, the preview graph's opaque
        /// pass owns its color / depth / renderer list resources (its input slots
        /// are intentionally unconnected — the pass allocates them from its
        /// parameters, ADR-017), and <c>finalBlit</c> receives the color target
        /// through the <c>opaque.ColorTargetOutput</c> slot connection.
        /// </summary>
        [Test]
        public void PreviewGraph_ConnectsKeySlots()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            List<Pass> result = asset.Build(renderer: null);

            var opaque = result.Find(p => p.PassName == "opaque") as DrawObjectPass;
            Assert.That(opaque, Is.Not.Null,
                "PreviewGraph should contain an 'opaque' DrawObjectPass.");
            Assert.That(opaque!.ColorTargetSlot!.IsConnected, Is.False,
                "opaque.ColorTarget should be unconnected — the pass allocates it locally.");
            Assert.That(opaque.DepthTargetSlot!.IsConnected, Is.False,
                "opaque.DepthTarget should be unconnected — the pass allocates it locally.");

            var finalBlit = result.Find(p => p.PassName == "finalBlit") as RenderOutputPass;
            Assert.That(finalBlit, Is.Not.Null,
                "PreviewGraph should contain a 'finalBlit' RenderOutputPass.");
            Assert.That(finalBlit!.ColorTargetSlot!.IsConnected, Is.True,
                "finalBlit.ColorTarget should be connected through opaque.ColorTargetOutput.");
        }

        /// <summary>
        /// PreviewGraph does NOT include heavy passes that are in StandardGraph:
        /// Build Light Data, Cluster Culling Light, Cluster Culling Probe,
        /// Builtin Sky, Transparency, Editor Wire Overlay.
        /// </summary>
        [Test]
        public void PreviewGraph_ExcludesHeavyPasses()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            List<Pass> passes = asset.Passes;

            // Collect all pass types for easy checking.
            var passTypes = new HashSet<Type>();
            foreach (Pass pass in passes)
            {
                passTypes.Add(pass.GetType());
            }

            Assert.That(passTypes.Contains(typeof(BuildLightDataPass)), Is.False,
                "PreviewGraph should NOT include Build Light Data (lighting is skipped).");
            Assert.That(passTypes.Contains(typeof(ClusterCullingLightPass)), Is.False,
                "PreviewGraph should NOT include Cluster Culling Light.");
            Assert.That(passTypes.Contains(typeof(ClusterCullingReflectionProbePass)), Is.False,
                "PreviewGraph should NOT include Cluster Culling Probe.");
            Assert.That(passTypes.Contains(typeof(BuiltinSkyPass)), Is.False,
                "PreviewGraph should NOT include Builtin Sky.");
            Assert.That(passTypes.Contains(typeof(EditorWireOverlayPass)), Is.False,
                "PreviewGraph should NOT include Editor Wire Overlay.");

            Assert.That(passTypes.Contains(typeof(DrawObjectPass)), Is.True,
                "PreviewGraph should include Draw Object (the opaque pass).");
            Assert.That(passTypes.Contains(typeof(RenderOutputPass)), Is.True,
                "PreviewGraph should include Render Output (the final blit pass).");
        }

        #endregion

        #region Resource Ownership

        /// <summary>
        /// The preview graph's opaque pass owns its render resources through
        /// parameters (ADR-017): a full-resolution color target, a 32-bit depth
        /// target, and an opaque renderer list. No separate resource node layer
        /// exists in the asset.
        /// </summary>
        [Test]
        public void PreviewGraph_OpaquePassOwnsResourceParameters()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            var opaque = (DrawObjectPass)asset.Passes.Find(p => p.PassName == "opaque");
            Assert.That(opaque, Is.Not.Null,
                "PreviewGraph should declare an 'opaque' DrawObjectPass.");

            Assert.That(opaque.ColorTargetParams.Width, Is.EqualTo(0),
                "Color target should use camera-scaled size.");
            Assert.That(opaque.DepthTargetParams.DepthBits,
                Is.EqualTo(UnityEngine.Rendering.DepthBits.Depth32),
                "Depth target should be 32-bit.");
            Assert.That(opaque.RendererListParams.ListKind, Is.EqualTo(RenderListKind.Opaque),
                "Renderer list should be opaque.");
        }

        #endregion

        #region Build — Runtime Pass Instantiation

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> successfully instantiates
        /// the preview pass set, all enabled by default.
        /// </summary>
        [Test]
        public void Build_FromPreviewGraph_InstantiatesAllPasses()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.GreaterThanOrEqualTo(2),
                "Build should instantiate at least the 2 preview passes.");
            Assert.That(result.TrueForAll(p => p.IsEnabled), Is.True,
                "All PreviewGraph passes should be enabled by default.");
        }

        #endregion

        #region Settings

        /// <summary>
        /// PreviewGraph.Settings has preview-appropriate values:
        /// SHEvalMode is PerVertex (default) and AllowHDR is false.
        /// </summary>
        [Test]
        public void Settings_HasPreviewAppropriateDefaults()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/PreviewGraph");
            Assume.That(asset, Is.Not.Null, "PreviewGraph asset must exist for this test.");

            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerVertex),
                "Preview should use PerVertex SH evaluation (simpler lighting).");
            Assert.That(asset.Settings.AllowHDR, Is.False,
                "Preview should not allocate HDR targets.");
        }

        #endregion
    }
}
