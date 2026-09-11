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
    /// 针对 <c>Runtime/Resources/RenderGraphs/StandardGraph.asset</c> 的测试。
    /// 验证资源能正确加载、<see cref="RenderGraphAsset.Build"/> 实例化预期的
    /// pass 集合、物化四个资源节点（color/depth 缓冲与两类渲染器列表）、
    /// 连接关键槽（资源节点加上光照/探针数据的槽连接链），并按拓扑排序 pass。
    /// </summary>
    public sealed class StandardGraphTests
    {
        /// <summary>
        /// StandardGraph 中各 pass 的预期实例名，按拓扑（依赖）序排列。
        /// </summary>
        private static readonly string[] ExpectedPassNames =
        {
            "buildLight", "drawShadow", "clusterProbe", "clusterLight",
            "forwardOpaque", "sky", "transparency", "wireOverlay", "finalBlit",
        };

        /// <summary>
        /// 每个测试前确保 <see cref="PassRegistry"/> 已填充
        /// （仅真实 pass —— 无 stub）。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        /// <summary>
        /// 每个测试后恢复干净的注册表。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PassRegistry.RegisterAll();
        }

        #region 资源加载

        /// <summary>
        /// <c>StandardGraph.asset</c> 可从 <c>Resources/RenderGraphs</c> 加载，
        /// 且是非空 <see cref="RenderGraphAsset"/>。
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

        #region Build —— Pass 组成

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> 在真实资源上成功执行，
        /// 且产出与预期完全一致、按拓扑序排列的 pass 集合。
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

        #region Build —— 连接

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> 之后，每个渲染 pass 的关键输入槽
        /// 均已连接。在 pass 自持资源模型（ADR-017）下，链头的 color/depth/
        /// 渲染器列表槽有意保持未连接 —— pass 从自身参数分配它们；
        /// 光照/探针数据通过 <see cref="SlotConnection"/> 的 pass 间链传递
        /// （例如 <c>buildLight.lightDatasBuffer</c> → <c>forwardOpaque.LightDatas</c>），
        /// 颜色目标链为 forwardOpaque → sky → transparency → wireOverlay /
        /// finalBlit。
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

            // ── forwardOpaque：链头 —— color/depth/渲染器列表本地分配
            //    （未连接）；光照/探针数据来自槽连接 ──

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
            Assert.That(forwardOpaque.CascadeShadowMapSlot!.IsConnected, Is.True,
                "forwardOpaque.ShadowMap should be connected through a slot connection from drawShadow.");

            // ── sky：color/depth 目标已连接（自 forwardOpaque 链式传入）──

            var sky = (BuiltinSkyPass)FindPass("sky");
            Assert.That(sky.ColorTargetSlot!.IsConnected, Is.True,
                "sky.ColorTarget should be connected through forwardOpaque.ColorTargetOutput.");
            Assert.That(sky.DepthTargetSlot!.IsConnected, Is.True,
                "sky.DepthTarget should be connected through forwardOpaque.DepthTargetOutput.");

            // ── transparency：color/depth 自 sky 链式传入，渲染器列表
            //    本地分配（未连接），光照/探针数据来自槽连接 ──

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
            Assert.That(transparency.CascadeShadowMapSlot!.IsConnected, Is.True,
                "transparency.ShadowMap should be connected through a slot connection from drawShadow.");

            // ── wireOverlay：color 目标已连接（自 transparency 链式传入）──

            var wireOverlay = (EditorWireOverlayPass)FindPass("wireOverlay");
            Assert.That(wireOverlay.ColorTargetSlot!.IsConnected, Is.True,
                "wireOverlay.ColorTarget should be connected through transparency.ColorTargetOutput.");

            // ── finalBlit：color 目标已连接（自 wireOverlay 链式传入）──

            var finalBlit = (RenderOutputPass)FindPass("finalBlit");
            Assert.That(finalBlit.ColorTargetSlot!.IsConnected, Is.True,
                "finalBlit.ColorTarget should be connected through wireOverlay.ColorTargetOutput.");
        }

        #endregion

        #region Build —— 拓扑排序

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> 按拓扑序返回 pass。
        /// 链式模型沿 color/depth 目标链添加显式 pass 间边，因此
        /// <c>forwardOpaque</c> 必须先于 <c>sky</c>，后者先于
        /// <c>transparency</c>，后者先于 <c>wireOverlay</c>，
        /// 后者先于 <c>finalBlit</c>；<c>buildLight</c> 通过槽连接向
        /// <c>clusterLight</c> 提供 LightDatas，因此也必须最先执行。
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

        #region 设置

        /// <summary>
        /// <see cref="RenderGraphAsset.Settings"/> 反映配置值：
        /// <see cref="RenderGraphSettings.SHEvalMode"/> 为 <c>PerPixel</c>，
        /// <see cref="RenderGraphSettings.AllowHDR"/> 为 <c>true</c>。
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
