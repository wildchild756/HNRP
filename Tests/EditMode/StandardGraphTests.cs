// <copyright file="StandardGraphTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using HN.HNRP;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <c>Runtime/Resources/RenderGraphs/StandardGraph.asset</c>.
    /// Verifies the asset loads correctly, <see cref="RenderGraphAsset.Build"/>
    /// instantiates the expected pass set, materializes the four resource nodes
    /// (color / depth buffers and both renderer lists), connects the key slots
    /// (resource nodes plus slot-connection chains for lighting / probe data),
    /// and orders passes topologically.
    /// </summary>
    public sealed class StandardGraphTests
    {
        /// <summary>
        /// The expected instance names of the passes in StandardGraph, in
        /// topological (dependency) order.
        /// </summary>
        private static readonly string[] ExpectedPassNames =
        {
            "buildLight", "clusterProbe", "clusterLight",
            "forwardOpaque", "sky", "transparency", "wireOverlay", "finalBlit",
        };

        /// <summary>
        /// Ensures <see cref="PassRegistry"/> is populated (real passes only —
        /// no stubs) before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        /// <summary>
        /// Restores the clean registry after each test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PassRegistry.RegisterAll();
        }

        #region Asset Loading

        /// <summary>
        /// <c>StandardGraph.asset</c> can be loaded from <c>Resources/RenderGraphs</c>
        /// and is a non-null <see cref="RenderGraphAsset"/>.
        /// </summary>
        [Test]
        public void Load_Asset_IsNotNullAndCorrectType()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/StandardGraph");

            Assert.That(asset, Is.Not.Null,
                "StandardGraph.asset should be loadable from Resources/RenderGraphs.");
            Assert.That(asset, Is.InstanceOf<RenderGraphAsset>(),
                "Loaded asset should be a RenderGraphAsset.");
        }

        #endregion

        #region Build — Pass Composition

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> succeeds on the real asset and
        /// produces exactly the expected pass set in topological order.
        /// </summary>
        [Test]
        public void Build_ProducesExpectedPasses()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/StandardGraph");
            Assume.That(asset, Is.Not.Null,
                "Test requires StandardGraph.asset to be loadable.");

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(ExpectedPassNames.Length),
                "Build should produce exactly the expected number of passes.");

            foreach (string name in ExpectedPassNames)
            {
                Assert.That(
                    result.Exists(p => p.PassName == name),
                    Is.True,
                    $"Pass '{name}' should be present in the build result.");
            }
        }

        #endregion

        #region Build — Connections

        /// <summary>
        /// After <see cref="RenderGraphAsset.Build"/>, the key input slots of
        /// every rendering pass are connected. Under the pass-owned resource
        /// model (ADR-017) the chain-head color / depth / renderer-list slots
        /// are intentionally unconnected — the pass allocates them from its own
        /// parameters; lighting / probe data flows through
        /// <see cref="SlotConnection"/> pass-to-pass chains (e.g.
        /// <c>buildLight.lightDatasBuffer</c> → <c>forwardOpaque.LightDatas</c>),
        /// and the color target chains forwardOpaque → sky → transparency →
        /// wireOverlay / finalBlit.
        /// </summary>
        [Test]
        public void Build_ConnectsKeyPassSlots()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/StandardGraph");
            Assume.That(asset, Is.Not.Null,
                "Test requires StandardGraph.asset to be loadable.");

            List<Pass> result = asset.Build(renderer: null);

            Pass FindPass(string instanceName)
            {
                Pass pass = result.Find(p => p.PassName == instanceName);
                Assert.That(pass, Is.Not.Null,
                    $"Pass '{instanceName}' should be present in the build result.");
                return pass!;
            }

            // ── forwardOpaque: chain head — color/depth/renderer-list are
            //    allocated locally (unconnected); lighting/probe data from slot
            //    connections ──

            var forwardOpaque = (DrawObjectPass)FindPass("forwardOpaque");
            Assert.That(forwardOpaque.ColorTargetSlot!.IsConnected, Is.False,
                "forwardOpaque.ColorTarget should be unconnected — the pass allocates it locally.");
            Assert.That(forwardOpaque.DepthTargetSlot!.IsConnected, Is.False,
                "forwardOpaque.DepthTarget should be unconnected — the pass allocates it locally.");
            Assert.That(forwardOpaque.LightDatasSlot!.IsConnected, Is.True,
                "forwardOpaque.LightDatas should be connected through a slot connection from buildLight.");
            Assert.That(forwardOpaque.ReflectionProbeAtlasSlot!.IsConnected, Is.True,
                "forwardOpaque.ReflectionProbeAtlas should be connected through a slot connection from clusterProbe.");
            Assert.That(forwardOpaque.ProbeMaskSlot!.IsConnected, Is.True,
                "forwardOpaque.ProbeMask should be connected through a slot connection from clusterProbe.");
            Assert.That(forwardOpaque.ProbeDatasSlot!.IsConnected, Is.True,
                "forwardOpaque.ProbeDatas should be connected through a slot connection from clusterProbe.");
            Assert.That(forwardOpaque.LightMaskSlot!.IsConnected, Is.True,
                "forwardOpaque.LightMask should be connected through a slot connection from clusterLight.");

            // ── sky: color / depth targets connected (chained from forwardOpaque) ──

            var sky = (BuiltinSkyPass)FindPass("sky");
            Assert.That(sky.ColorTargetSlot!.IsConnected, Is.True,
                "sky.ColorTarget should be connected through forwardOpaque.ColorTargetOutput.");
            Assert.That(sky.DepthTargetSlot!.IsConnected, Is.True,
                "sky.DepthTarget should be connected through forwardOpaque.DepthTargetOutput.");

            // ── transparency: color/depth chained from sky, renderer list
            //    allocated locally (unconnected), lighting/probe data from slot
            //    connections ──

            var transparency = (DrawObjectPass)FindPass("transparency");
            Assert.That(transparency.ColorTargetSlot!.IsConnected, Is.True,
                "transparency.ColorTarget should be connected through sky.ColorTargetOutput.");
            Assert.That(transparency.DepthTargetSlot!.IsConnected, Is.True,
                "transparency.DepthTarget should be connected through sky.DepthTargetOutput.");
            Assert.That(transparency.LightDatasSlot!.IsConnected, Is.True,
                "transparency.LightDatas should be connected through a slot connection from buildLight.");
            Assert.That(transparency.ReflectionProbeAtlasSlot!.IsConnected, Is.True,
                "transparency.ReflectionProbeAtlas should be connected through a slot connection from clusterProbe.");
            Assert.That(transparency.ProbeMaskSlot!.IsConnected, Is.True,
                "transparency.ProbeMask should be connected through a slot connection from clusterProbe.");
            Assert.That(transparency.ProbeDatasSlot!.IsConnected, Is.True,
                "transparency.ProbeDatas should be connected through a slot connection from clusterProbe.");
            Assert.That(transparency.LightMaskSlot!.IsConnected, Is.True,
                "transparency.LightMask should be connected through a slot connection from clusterLight.");

            // ── wireOverlay: color target connected (chained from transparency) ──

            var wireOverlay = (EditorWireOverlayPass)FindPass("wireOverlay");
            Assert.That(wireOverlay.ColorTargetSlot!.IsConnected, Is.True,
                "wireOverlay.ColorTarget should be connected through transparency.ColorTargetOutput.");

            // ── finalBlit: color target connected (chained from wireOverlay) ──

            var finalBlit = (RenderOutputPass)FindPass("finalBlit");
            Assert.That(finalBlit.ColorTargetSlot!.IsConnected, Is.True,
                "finalBlit.ColorTarget should be connected through wireOverlay.ColorTargetOutput.");
        }

        #endregion

        #region Build — Topological Order

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> returns passes in topological
        /// order. The chained model adds explicit pass-to-pass edges along the
        /// color / depth target chain, so <c>forwardOpaque</c> must run before
        /// <c>sky</c>, which runs before <c>transparency</c>, which runs before
        /// <c>wireOverlay</c>, which runs before <c>finalBlit</c>;
        /// <c>buildLight</c> feeds LightDatas to <c>clusterLight</c> through a
        /// slot connection, so it must run first too.
        /// </summary>
        [Test]
        public void Build_OrdersPassesTopologically()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/StandardGraph");
            Assume.That(asset, Is.Not.Null,
                "Test requires StandardGraph.asset to be loadable.");

            List<Pass> result = asset.Build(renderer: null);

            int IndexOf(string name) => result.FindIndex(p => p.PassName == name);

            Assert.That(IndexOf("buildLight"), Is.GreaterThanOrEqualTo(0),
                "buildLight should be present.");
            Assert.That(IndexOf("clusterLight"), Is.GreaterThanOrEqualTo(0),
                "clusterLight should be present.");
            Assert.That(IndexOf("forwardOpaque"), Is.GreaterThanOrEqualTo(0),
                "forwardOpaque should be present.");
            Assert.That(IndexOf("sky"), Is.GreaterThanOrEqualTo(0),
                "sky should be present.");
            Assert.That(IndexOf("transparency"), Is.GreaterThanOrEqualTo(0),
                "transparency should be present.");
            Assert.That(IndexOf("wireOverlay"), Is.GreaterThanOrEqualTo(0),
                "wireOverlay should be present.");
            Assert.That(IndexOf("finalBlit"), Is.GreaterThanOrEqualTo(0),
                "finalBlit should be present.");

            Assert.That(IndexOf("buildLight"), Is.LessThan(IndexOf("clusterLight")),
                "buildLight (feeds LightDatas via slot connection) must be ordered before clusterLight.");

            Assert.That(IndexOf("forwardOpaque"), Is.LessThan(IndexOf("sky")),
                "forwardOpaque (upstream color/depth pass) must be ordered before sky.");
            Assert.That(IndexOf("sky"), Is.LessThan(IndexOf("transparency")),
                "sky (upstream color/depth pass) must be ordered before transparency.");
            Assert.That(IndexOf("transparency"), Is.LessThan(IndexOf("wireOverlay")),
                "transparency (upstream color pass) must be ordered before wireOverlay.");
            Assert.That(IndexOf("wireOverlay"), Is.LessThan(IndexOf("finalBlit")),
                "wireOverlay (upstream color pass) must be ordered before finalBlit.");

            Assert.That(IndexOf("forwardOpaque"), Is.LessThan(IndexOf("transparency")),
                "forwardOpaque must be ordered before transparency (stable definition order).");
        }

        #endregion

        #region Settings

        /// <summary>
        /// <see cref="RenderGraphAsset.Settings"/> reflect the configured values:
        /// <see cref="RenderGraphSettings.SHEvalMode"/> is <c>PerPixel</c>,
        /// <see cref="RenderGraphSettings.AllowHDR"/> is <c>true</c>.
        /// </summary>
        [Test]
        public void Settings_HasCorrectValues()
        {
            var asset = Resources.Load<RenderGraphAsset>("RenderGraphs/StandardGraph");
            Assume.That(asset, Is.Not.Null);

            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerPixel),
                "Standard graph should use PerPixel SH evaluation.");
            Assert.That(asset.Settings.AllowHDR, Is.True,
                "Standard graph should allow HDR.");
        }

        #endregion
    }
}
