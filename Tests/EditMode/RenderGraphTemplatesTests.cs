// <copyright file="RenderGraphTemplatesTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HN.HNRP;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HN.HNRP.Tests
{
    /// <summary>
    /// <see cref="RenderGraphTemplates"/> 的测试 —— 验证每个模板的构建代码
    /// （<see cref="RenderGraphTemplate.CreateBlueprint"/>，编辑器/运行时共享的
    /// 唯一 pass 创建代码，方案 X）声明的 pass/连接/设置与预期完全一致，
    /// 并验证 <see cref="RenderGraphTemplate.Ensure"/> 返回持久化且缓存的
    /// <see cref="RenderGraphAsset"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RenderGraphTemplate.Ensure"/> 返回持久化资源 ——
    /// 测试不得对其调用 <see cref="Object.DestroyImmediate"/>。
    /// </para>
    /// <para>
    /// 这些测试只覆盖模板构建代码与 Ensure 机制，
    /// 不会调用 <see cref="RenderGraphBuilder.Build"/>。
    /// </para>
    /// </remarks>
    public sealed class RenderGraphTemplatesTests
    {
        #region Ensure —— 非空且唯一

        /// <summary>
        /// <see cref="RenderGraphTemplates.Standard"/>.Ensure() 返回非空资源，
        /// 重复调用返回同一缓存实例。
        /// </summary>
        [Test]
        public void StandardTemplate_Ensure_ReturnsNonNullAndUnique()
        {
            RenderGraphAsset first = RenderGraphTemplates.Standard.Ensure();
            RenderGraphAsset second = RenderGraphTemplates.Standard.Ensure();

            Assert.That(first, Is.Not.Null,
                "Standard template Ensure() should return a non-null render graph asset.");
            Assert.That(second, Is.SameAs(first),
                "Standard template Ensure() should return the same cached asset on repeated calls.");
        }

        /// <summary>
        /// <see cref="RenderGraphTemplates.Preview"/>.Ensure() 返回非空资源，
        /// 重复调用返回同一缓存实例。
        /// </summary>
        [Test]
        public void PreviewTemplate_Ensure_ReturnsNonNullAndUnique()
        {
            RenderGraphAsset first = RenderGraphTemplates.Preview.Ensure();
            RenderGraphAsset second = RenderGraphTemplates.Preview.Ensure();

            Assert.That(first, Is.Not.Null,
                "Preview template Ensure() should return a non-null render graph asset.");
            Assert.That(second, Is.SameAs(first),
                "Preview template Ensure() should return the same cached asset on repeated calls.");
        }

        /// <summary>
        /// <see cref="RenderGraphTemplates.Reflection"/>.Ensure() 返回非空资源，
        /// 重复调用返回同一缓存实例。
        /// </summary>
        [Test]
        public void ReflectionTemplate_Ensure_ReturnsNonNullAndUnique()
        {
            RenderGraphAsset first = RenderGraphTemplates.Reflection.Ensure();
            RenderGraphAsset second = RenderGraphTemplates.Reflection.Ensure();

            Assert.That(first, Is.Not.Null,
                "Reflection template Ensure() should return a non-null render graph asset.");
            Assert.That(second, Is.SameAs(first),
                "Reflection template Ensure() should return the same cached asset on repeated calls.");
        }

        #endregion

        #region Blueprint —— 定义内容

        /// <summary>
        /// Standard 模板声明完整 8-pass 管线（buildLight / clusterProbe /
        /// clusterLight / forwardOpaque / sky / transparency / wireOverlay /
        /// finalBlit）、链式槽连接与 PerPixel HDR 设置。
        /// 透明 pass 分配透明渲染器列表。
        /// </summary>
        [Test]
        public void StandardTemplate_HasExpectedDefinition()
        {
            RenderGraphBlueprint bp = RenderGraphTemplates.Standard.CreateBlueprint();

            Assert.That(bp.Passes.Count, Is.EqualTo(8),
                "Standard template should declare exactly 8 passes.");
            Assert.That(bp.Connections.Count, Is.EqualTo(18),
                "Standard template should declare exactly 18 slot connections.");

            Assert.That(bp.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerPixel),
                "Standard template should use PerPixel SH evaluation.");
            Assert.That(bp.Settings.AllowHDR, Is.True,
                "Standard template should allow HDR render targets.");

            foreach (string passName in new[] { "buildLight", "forwardOpaque", "finalBlit" })
            {
                Assert.That(bp.Passes.Any(p => p.PassName == passName), Is.True,
                    $"Standard template should declare a '{passName}' pass.");
            }

            // 渲染器列表参数在 pass 构造时配置：
            // forwardOpaque 使用 opaque，transparency 使用 transparent。
            var forwardOpaque = (DrawObjectPass)bp.Passes.First(p => p.PassName == "forwardOpaque");
            var transparency = (DrawObjectPass)bp.Passes.First(p => p.PassName == "transparency");

            Assert.That(forwardOpaque.RendererListParams.ListKind, Is.EqualTo(RenderListKind.Opaque),
                "forwardOpaque should allocate an opaque renderer list.");
            Assert.That(transparency.RendererListParams.ListKind, Is.EqualTo(RenderListKind.Transparent),
                "transparency should allocate a transparent renderer list.");

            Assert.That(
                bp.Connections.Any(c =>
                    c.SourcePass == "forwardOpaque"
                    && c.SourceSlot == "ColorTargetOutput"
                    && c.TargetPass == "sky"
                    && c.TargetSlot == "ColorTarget"),
                Is.True,
                "Standard template should connect forwardOpaque.ColorTargetOutput to sky.ColorTarget.");
        }

        /// <summary>
        /// Preview 模板声明最小 2-pass 管线（opaque / finalBlit）
        /// 与单条链式连接。
        /// </summary>
        [Test]
        public void PreviewTemplate_HasExpectedDefinition()
        {
            RenderGraphBlueprint bp = RenderGraphTemplates.Preview.CreateBlueprint();

            Assert.That(bp.Passes.Count, Is.EqualTo(2),
                "Preview template should declare exactly 2 passes.");
            Assert.That(bp.Connections.Count, Is.EqualTo(1),
                "Preview template should declare exactly 1 slot connection.");

            foreach (string passName in new[] { "opaque", "finalBlit" })
            {
                Assert.That(bp.Passes.Any(p => p.PassName == passName), Is.True,
                    $"Preview template should declare a '{passName}' pass.");
            }

            var opaque = (DrawObjectPass)bp.Passes.First(p => p.PassName == "opaque");
            Assert.That(opaque.RendererListParams.ListKind, Is.EqualTo(RenderListKind.Opaque),
                "Preview opaque pass should allocate an opaque renderer list.");
        }

        /// <summary>
        /// Reflection 模板声明 7-pass 反射管线与 11 条槽连接。
        /// 它不包含 cluster probe pass：Reflection 图渲染探针面，
        /// 但不得自行渲染反射探针。
        /// </summary>
        [Test]
        public void ReflectionTemplate_HasExpectedDefinition()
        {
            RenderGraphBlueprint bp = RenderGraphTemplates.Reflection.CreateBlueprint();

            Assert.That(bp.Passes.Count, Is.EqualTo(7),
                "Reflection template should declare exactly 7 passes.");
            Assert.That(bp.Connections.Count, Is.EqualTo(11),
                "Reflection template should declare exactly 11 slot connections.");

            Assert.That(bp.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerPixel),
                "Reflection template should use PerPixel SH evaluation.");
            Assert.That(bp.Settings.AllowHDR, Is.True,
                "Reflection template should allow HDR render targets.");

            foreach (string passName in new[] { "buildLight", "forwardOpaque", "transparency", "finalBlit" })
            {
                Assert.That(bp.Passes.Any(p => p.PassName == passName), Is.True,
                    $"Reflection template should declare a '{passName}' pass.");
            }

            // 不包含反射探针 cluster-culling pass：Reflection 图渲染
            // 探针面但不得渲染探针自身。
            Assert.That(bp.Passes.Any(p => p.PassName == "clusterProbe"), Is.False,
                "Reflection template must not declare a cluster probe pass.");
        }

        #endregion

        #region Ensure —— 资源与模板绑定

        /// <summary>
        /// Ensure() 创建/返回持久化资源，其
        /// <see cref="RenderGraphAsset.Kind"/> 与
        /// <see cref="RenderGraphAsset.Settings"/> 与模板构建代码一致。
        /// </summary>
        [Test]
        public void Ensure_AssetMatchesTemplate()
        {
            RenderGraphAsset asset = RenderGraphTemplates.Standard.Ensure();

            Assert.That(asset.Kind, Is.EqualTo(RenderGraphKind.Standard),
                "Ensure() should bind the asset to the Standard template kind.");
            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerPixel),
                "Ensure() should copy the Standard settings onto the asset.");
            Assert.That(asset.Settings.AllowHDR, Is.True,
                "Ensure() should copy the Standard HDR setting onto the asset.");
        }

        /// <summary>
        /// Standard、Reflection、Preview 模板解析到不同的持久化资源 ——
        /// 不得共享缓存实例。
        /// </summary>
        [Test]
        public void StandardAndPreview_AreDistinct()
        {
            RenderGraphAsset standard = RenderGraphTemplates.Standard.Ensure();
            RenderGraphAsset preview = RenderGraphTemplates.Preview.Ensure();
            RenderGraphAsset reflection = RenderGraphTemplates.Reflection.Ensure();

            Assert.That(standard, Is.Not.SameAs(preview),
                "Standard and Preview templates should resolve to distinct render graph assets.");
            Assert.That(reflection, Is.Not.SameAs(standard),
                "Reflection and Standard templates should resolve to distinct render graph assets.");
            Assert.That(reflection, Is.Not.SameAs(preview),
                "Reflection and Preview templates should resolve to distinct render graph assets.");
        }

        #endregion

#if UNITY_EDITOR
        #region Ensure —— 非改写守卫

        /// <summary>
        /// <see cref="RenderGraphTemplate.Ensure"/> 不得覆盖已指向模板 kind 的
        /// 资源（保护设置编辑）。直接加载现有 Standard 资源、记录其设置、
        /// 调用 Ensure，并断言 kind/设置未变化。
        /// </summary>
        [Test]
        public void TemplateDoesNotMutate_ExistingMatchingAsset()
        {
            RenderGraphAsset asset =
                AssetDatabase.LoadAssetAtPath<RenderGraphAsset>(RenderGraphTemplates.Standard.AssetPath);
            Assume.That(asset, Is.Not.Null,
                "Standard template asset must already exist for the non-overwrite guard to be testable.");
            Assume.That(asset.Kind, Is.EqualTo(RenderGraphKind.Standard),
                "Standard template asset must already point at the Standard kind.");

            RenderGraphSettings settingsBefore = asset.Settings;

            RenderGraphAsset ensured = RenderGraphTemplates.Standard.Ensure();

            Assert.That(ensured, Is.SameAs(asset),
                "Ensure() should return the existing asset rather than replacing it.");
            Assert.That(asset.Kind, Is.EqualTo(RenderGraphKind.Standard),
                "Ensure() must keep the asset bound to the Standard kind.");
            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(settingsBefore.SHEvalMode),
                "Ensure() must not overwrite the asset's SH eval mode when kind already matches.");
            Assert.That(asset.Settings.AllowHDR, Is.EqualTo(settingsBefore.AllowHDR),
                "Ensure() must not overwrite the asset's AllowHDR when kind already matches.");
        }

        #endregion
#endif
    }
}
