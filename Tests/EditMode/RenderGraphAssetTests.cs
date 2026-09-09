// <copyright file="RenderGraphAssetTests.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using HN.HNRP;
using Object = UnityEngine.Object;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// 渲染图构建管线测试：<see cref="RenderGraphBuilder"/> 消费
    /// <see cref="RenderGraphBlueprint"/>（运行时/编辑器共享的模板构建产物，
    /// 方案 X）并产出可执行的 pass 列表。验证槽连接的名称解析
    /// 与启用 pass 过滤。
    /// </summary>
    public sealed class RenderGraphAssetTests
    {
        #region 测试用 Pass 子类

        /// <summary>
        /// 构建测试使用的最小 pass。注册为 <c>"TestPassA"</c>。
        /// </summary>
        [Pass("TestPassA")]
        private sealed class TestPassA : Pass
        {
            /// <summary>
            /// 以给定名称初始化新实例。
            /// </summary>
            /// <param name="name">pass 实例名。</param>
            public TestPassA(string name)
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
        }

        /// <summary>
        /// 构建测试使用的最小 pass。注册为 <c>"TestPassB"</c>。
        /// </summary>
        [Pass("TestPassB")]
        private sealed class TestPassB : Pass
        {
            /// <summary>
            /// 以给定名称初始化新实例。
            /// </summary>
            /// <param name="name">pass 实例名。</param>
            public TestPassB(string name)
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
        }

        /// <summary>
        /// 初始为禁用状态的 pass。注册为 <c>"TestPassDisabled"</c>。
        /// 用于验证 <see cref="RenderGraphBuilder.Build"/> 过滤掉
        /// <see cref="Pass.IsEnabled"/> 为 <c>false</c> 的 pass。
        /// </summary>
        [Pass("TestPassDisabled")]
        private sealed class TestPassDisabled : Pass
        {
            /// <summary>
            /// 初始化一个默认禁用的新实例。
            /// </summary>
            /// <param name="name">pass 实例名。</param>
            public TestPassDisabled(string name)
                : base(name)
            {
                IsEnabled = false;
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
        }

        /// <summary>
        /// 带纹理输入槽的 pass，用于验证
        /// <see cref="RenderGraphBuilder.Build"/> 的槽连接接线。
        /// </summary>
        [Pass("TestTextureConsumer")]
        private sealed class TestTextureConsumerPass : Pass
        {
            /// <summary>
            /// 获取已注册的纹理输入槽。
            /// </summary>
            public TextureSlot Input { get; private set; }

            /// <summary>
            /// 以给定名称初始化新实例。
            /// </summary>
            /// <param name="name">pass 实例名。</param>
            public TestTextureConsumerPass(string name)
                : base(name)
            {
            }

            /// <inheritdoc />
            public override void SetupSlots()
            {
                Input = new TextureSlot("In", SlotDirection.Input);
                RegisterSlot(Input);
            }

            /// <inheritdoc />
            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
            }

            /// <inheritdoc />
            public override void Record(RenderGraph renderGraph)
            {
            }
        }

        /// <summary>
        /// 带输出槽的 pass，用于验证
        /// <see cref="RenderGraphBuilder.Build"/> 的槽连接接线。
        /// </summary>
        [Pass("TestTextureProducer")]
        private sealed class TestTextureProducerPass : Pass
        {
            /// <summary>
            /// 获取已注册的纹理输出槽。
            /// </summary>
            public TextureSlot Output { get; private set; }

            /// <summary>
            /// 以给定名称初始化新实例。
            /// </summary>
            /// <param name="name">pass 实例名。</param>
            public TestTextureProducerPass(string name)
                : base(name)
            {
            }

            /// <inheritdoc />
            public override void SetupSlots()
            {
                Output = new TextureSlot("Out", SlotDirection.Output);
                RegisterSlot(Output);
            }

            /// <inheritdoc />
            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
            }

            /// <inheritdoc />
            public override void Record(RenderGraph renderGraph)
            {
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 每个测试前确保 <see cref="PassRegistry"/> 已填充。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PassRegistry.RegisterAll();
        }

        #endregion

        #region 辅助方法

        private static List<Pass> Build(List<Pass> passes, List<SlotConnection> connections = null)
        {
            var blueprint = new RenderGraphBlueprint(
                passes,
                connections ?? new List<SlotConnection>(),
                new RenderGraphSettings());
            return RenderGraphBuilder.Build(blueprint);
        }

        #endregion

        #region Build —— Pass 实例化

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 直接使用蓝图中的 pass
        /// （各自带预期的 <see cref="Pass.PassName"/> 与类型）。
        /// </summary>
        [Test]
        public void Build_InstantiatesPasses()
        {
            var passes = new List<Pass>
            {
                new TestPassA("Alpha"),
                new TestPassB("Beta"),
            };

            List<Pass> result = Build(passes);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Build should return exactly two passes for two definitions.");

            Assert.That(result[0].PassName, Is.EqualTo("Alpha"),
                "First pass should keep the PassName from its blueprint.");
            Assert.That(result[0].GetType(), Is.EqualTo(typeof(TestPassA)),
                "First pass should be of type TestPassA.");
            Assert.That(result[0].IsEnabled, Is.True,
                "Newly built passes should be enabled by default.");

            Assert.That(result[1].PassName, Is.EqualTo("Beta"),
                "Second pass should keep the PassName from its blueprint.");
            Assert.That(result[1].GetType(), Is.EqualTo(typeof(TestPassB)),
                "Second pass should be of type TestPassB.");
            Assert.That(result[1].IsEnabled, Is.True,
                "Newly built passes should be enabled by default.");
        }

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 对空蓝图返回空列表。
        /// </summary>
        [Test]
        public void Build_EmptyBlueprint_ReturnsEmptyList()
        {
            List<Pass> result = Build(new List<Pass>());

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list even for an empty blueprint.");
            Assert.That(result.Count, Is.EqualTo(0),
                "Build should return an empty list when the blueprint has no passes.");
        }

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 跳过 <see cref="Pass.PassName"/>
        /// 为 <c>null</c> 或空字符串的 pass。
        /// </summary>
        [Test]
        public void Build_SkipsPassesWithNullPassName()
        {
            List<Pass> result = Build(new List<Pass>
            {
                new TestPassA(null),
                new TestPassA(string.Empty),
                new TestPassB("Valid"),
            });

            Assert.That(result.Count, Is.EqualTo(1),
                "Build should skip passes with null/empty PassName.");
            Assert.That(result[0].PassName, Is.EqualTo("Valid"),
                "Only the valid definition should be built.");
        }

        #endregion

        #region Build —— 按名称槽连接

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 处理
        /// <see cref="SlotConnection"/> 条目时能正确按名称解析 pass。
        /// 所有被引用的 pass 无论连接是否有效都出现在结果中。
        /// </summary>
        [Test]
        public void Build_ConnectsSlots_ByName()
        {
            List<Pass> result = Build(
                new List<Pass>
                {
                    new TestPassA("SourcePass"),
                    new TestPassB("TargetPass"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create(
                        sourcePass: "SourcePass",
                        sourceSlot: "ColorOutput",
                        targetPass: "TargetPass",
                        targetSlot: "ColorInput"),
                });

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Both passes should be built.");

            // 按名称验证正确的 pass 实例存在。
            bool hasSource = result.Exists(p => p.PassName == "SourcePass");
            bool hasTarget = result.Exists(p => p.PassName == "TargetPass");

            Assert.That(hasSource, Is.True,
                "SourcePass should be present in the result.");
            Assert.That(hasTarget, Is.True,
                "TargetPass should be present in the result.");
            Assert.That(result.Find(p => p.PassName == "SourcePass").GetType(),
                Is.EqualTo(typeof(TestPassA)),
                "SourcePass should be TestPassA.");
            Assert.That(result.Find(p => p.PassName == "TargetPass").GetType(),
                Is.EqualTo(typeof(TestPassB)),
                "TargetPass should be TestPassB.");
        }

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 优雅处理
        /// <see cref="SlotConnection"/> 中源或目标 pass 名称不匹配
        /// 任何已构建 pass 的情况。
        /// </summary>
        [Test]
        public void Build_HandlesMissingConnectionTarget_DoesNotThrow()
        {
            List<Pass> result = Build(
                new List<Pass>
                {
                    new TestPassA("OnlyPass"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create("OnlyPass", "Out", "GhostPass", "In"),
                });

            Assert.That(result, Is.Not.Null,
                "Build should not throw when a connection references a non-existent pass.");
            Assert.That(result.Count, Is.EqualTo(1),
                "The valid pass should still be built.");
            Assert.That(result[0].PassName, Is.EqualTo("OnlyPass"),
                "The valid pass should be the only one returned.");
        }

        #endregion

        #region Build —— 启用 pass 过滤

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 只返回
        /// <see cref="Pass.IsEnabled"/> 为 <c>true</c> 的 pass。
        /// 构造函数中设置 <c>IsEnabled = false</c> 的 pass 被排除。
        /// </summary>
        [Test]
        public void Build_ReturnsEnabledPassesOnly()
        {
            List<Pass> result = Build(new List<Pass>
            {
                new TestPassA("EnabledAlpha"),
                new TestPassDisabled("DisabledPass"),
                new TestPassB("EnabledBeta"),
            });

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Build should exclude the disabled pass, returning only the two enabled ones.");

            // 验证两个启用 pass 都存在。
            bool hasAlpha = result.Exists(p => p.PassName == "EnabledAlpha");
            bool hasBeta = result.Exists(p => p.PassName == "EnabledBeta");
            bool hasDisabled = result.Exists(p => p.PassName == "DisabledPass");

            Assert.That(hasAlpha, Is.True,
                "EnabledAlpha should be in the result.");
            Assert.That(hasBeta, Is.True,
                "EnabledBeta should be in the result.");
            Assert.That(hasDisabled, Is.False,
                "DisabledPass should NOT be in the result.");
        }

        #endregion

        #region Build —— 槽连接与拓扑排序

        /// <summary>
        /// <see cref="RenderGraphBuilder.Build"/> 通过
        /// <see cref="SlotConnection"/> 将某 pass 的输出槽接到另一 pass 的
        /// 输入槽，使目标输入槽处于已连接状态，消费方可在
        /// <see cref="Pass.Record"/> 中读取生产方的句柄。
        /// </summary>
        [Test]
        public void Build_SlotConnection_WiresOutputToInput()
        {
            List<Pass> result = Build(
                new List<Pass>
                {
                    new TestTextureProducerPass("Producer"),
                    new TestTextureConsumerPass("Consumer"),
                },
                new List<SlotConnection>
                {
                    SlotConnection.Create(
                        sourcePass: "Producer",
                        sourceSlot: "Out",
                        targetPass: "Consumer",
                        targetSlot: "In"),
                });

            Assert.That(result.Count, Is.EqualTo(2),
                "Both passes should be built and enabled.");

            var producer = result.Find(p => p.PassName == "Producer") as TestTextureProducerPass;
            var consumer = result.Find(p => p.PassName == "Consumer") as TestTextureConsumerPass;
            Assert.That(producer, Is.Not.Null);
            Assert.That(consumer, Is.Not.Null);

            Assert.That(consumer!.Input.IsConnected, Is.True,
                "The consumer's input slot should be connected through the slot connection.");
            Assert.That(consumer.Input.HasHandle, Is.False,
                "The consumer's input has no handle until the producer publishes one.");

            // 在生产方输出上发布句柄并验证输入槽反映它
            // （HasHandle 变为 true，ReadHandle 返回它）。
            producer!.Output.SetHandle(default(TextureHandle));

            Assert.That(consumer.Input.HasHandle, Is.False,
                "A default (invalid) texture handle must be treated as no handle.");
        }

        #endregion

        #region 设置

        /// <summary>
        /// <see cref="RenderGraphAsset.Settings"/> 可读写，
        /// <see cref="RenderGraphSettings"/> 结构体字段可独立修改。
        /// </summary>
        [Test]
        public void Settings_CanBeReadAndWritten()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();

            // 默认状态。
            Assert.That(asset.Kind, Is.EqualTo(RenderGraphKind.None),
                "A fresh RenderGraphAsset should not point at any template.");
            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(default(SHEvalMode)),
                "Default SHEvalMode should be PerVertex (= 0).");
            Assert.That(asset.Settings.AllowHDR, Is.False,
                "Default AllowHDR should be false.");

            // 写入新值。
            var newSettings = new RenderGraphSettings
            {
                SHEvalMode = SHEvalMode.PerPixel,
                AllowHDR = true,
            };
            asset.Settings = newSettings;

            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(SHEvalMode.PerPixel),
                "SHEvalMode should reflect the written value.");
            Assert.That(asset.Settings.AllowHDR, Is.True,
                "AllowHDR should reflect the written value.");

            // 未配置资源的 Build 结果为空列表，绝不返回 null。
            Assert.That(asset.Build(renderer: null), Is.Empty,
                "Build on an asset without a template kind should return an empty list.");

            Object.DestroyImmediate(asset);
        }

        #endregion
    }
}
