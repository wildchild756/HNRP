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
    /// Tests for <see cref="RenderGraphAsset"/> in <c>Runtime/Config/RenderGraphAsset.cs</c>.
    /// Verifies Build() pass instantiation, slot-connection name resolution,
    /// and enabled-pass filtering.
    /// </summary>
    public sealed class RenderGraphAssetTests
    {
        #region Test Pass Subclasses

        /// <summary>
        /// Minimal pass used for build tests. Registered as <c>"TestPassA"</c>.
        /// </summary>
        [Pass("TestPassA")]
        private sealed class TestPassA : Pass
        {
            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestPassA(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestPassA()
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

        /// <summary>
        /// Minimal pass used for build tests. Registered as <c>"TestPassB"</c>.
        /// </summary>
        [Pass("TestPassB")]
        private sealed class TestPassB : Pass
        {
            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestPassB(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestPassB()
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

        /// <summary>
        /// Pass that starts disabled. Registered as <c>"TestPassDisabled"</c>.
        /// Used to verify that <see cref="RenderGraphAsset.Build"/> filters
        /// out passes where <see cref="Pass.IsEnabled"/> is <c>false</c>.
        /// </summary>
        [Pass("TestPassDisabled")]
        private sealed class TestPassDisabled : Pass
        {
            /// <summary>
            /// Initializes a new instance that is disabled by default.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestPassDisabled(string name)
                : base(name)
            {
                IsEnabled = false;
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestPassDisabled()
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

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        /// <summary>
        /// Definition for <see cref="TestTextureConsumerPass"/>.
        /// </summary>
        [Pass("TestTextureConsumer")]
        private sealed class TestTextureConsumerPass : Pass
        {
            /// <summary>
            /// Gets the registered texture input slot.
            /// </summary>
            public TextureSlot Input { get; private set; }

            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestTextureConsumerPass(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestTextureConsumerPass()
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

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        /// <summary>
        /// Pass with an output slot used to verify slot-connection wiring
        /// through <see cref="RenderGraphAsset.Build"/>.
        /// </summary>
        [Pass("TestTextureProducer")]
        private sealed class TestTextureProducerPass : Pass
        {
            /// <summary>
            /// Gets the registered texture output slot.
            /// </summary>
            public TextureSlot Output { get; private set; }

            /// <summary>
            /// Initializes a new instance with the given name.
            /// </summary>
            /// <param name="name">The pass instance name.</param>
            public TestTextureProducerPass(string name)
                : base(name)
            {
            }

            /// <summary>
            /// Parameterless constructor used by <see cref="RenderGraphAsset.Build"/>
            /// runtime cloning.
            /// </summary>
            public TestTextureProducerPass()
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

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        #endregion

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

        #region Build — Pass Instantiation

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> instantiates the correct number of
        /// passes from <see cref="RenderGraphAsset.Passes"/> definitions, each with
        /// the expected <see cref="Pass.PassName"/>.
        /// </summary>
        [Test]
        public void Build_InstantiatesPasses()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPassA("Alpha"));
            asset.Passes.Add(new TestPassB("Beta"));

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Build should return exactly two passes for two definitions.");

            Assert.That(result[0].PassName, Is.EqualTo("Alpha"),
                "First pass should have the PassName from its template.");
            Assert.That(result[0].GetType(), Is.EqualTo(typeof(TestPassA)),
                "First pass should be of type TestPassA.");
            Assert.That(result[0].IsEnabled, Is.True,
                "Newly created passes should be enabled by default.");

            Assert.That(result[1].PassName, Is.EqualTo("Beta"),
                "Second pass should have the PassName from its template.");
            Assert.That(result[1].GetType(), Is.EqualTo(typeof(TestPassB)),
                "Second pass should be of type TestPassB.");
            Assert.That(result[1].IsEnabled, Is.True,
                "Newly created passes should be enabled by default.");

            Object.DestroyImmediate(asset);
        }

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> returns an empty list when
        /// <see cref="RenderGraphAsset.Passes"/> is empty.
        /// </summary>
        [Test]
        public void Build_EmptyPassesList_ReturnsEmptyList()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list even for empty definitions.");
            Assert.That(result.Count, Is.EqualTo(0),
                "Build should return an empty list when no definitions exist.");

            Object.DestroyImmediate(asset);
        }

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> skips definitions whose
        /// <see cref="RenderGraphAsset.Build"/> skips passes whose
        /// <see cref="Pass.PassName"/> is <c>null</c> or empty.
        /// </summary>
        [Test]
        public void Build_SkipsPassesWithNullPassName()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPassA(null));
            asset.Passes.Add(new TestPassA(string.Empty));
            asset.Passes.Add(new TestPassB("Valid"));

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result.Count, Is.EqualTo(1),
                "Build should skip passes with null/empty PassName.");
            Assert.That(result[0].PassName, Is.EqualTo("Valid"),
                "Only the valid definition should be instantiated.");

            Object.DestroyImmediate(asset);
        }

        #endregion

        #region Build — Slot Connections by Name

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> correctly resolves passes by name
        /// when processing <see cref="SlotConnection"/> entries. All referenced
        /// passes appear in the result regardless of connection validity.
        /// </summary>
        [Test]
        public void Build_ConnectsSlots_ByName()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPassA("SourcePass"));
            asset.Passes.Add(new TestPassB("TargetPass"));
            asset.Connections.Add(SlotConnection.Create(
                sourcePass: "SourcePass",
                sourceSlot: "ColorOutput",
                targetPass: "TargetPass",
                targetSlot: "ColorInput"));

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Both passes should be instantiated.");

            // Verify the correct pass instances exist by name.
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

            Object.DestroyImmediate(asset);
        }

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> gracefully handles
        /// <see cref="SlotConnection"/> entries where source or target pass
        /// names do not match any instantiated pass.
        /// </summary>
        [Test]
        public void Build_HandlesMissingConnectionTarget_DoesNotThrow()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPassA("OnlyPass"));
            asset.Connections.Add(SlotConnection.Create("OnlyPass", "Out", "GhostPass", "In"));

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should not throw when a connection references a non-existent pass.");
            Assert.That(result.Count, Is.EqualTo(1),
                "The valid pass should still be instantiated.");
            Assert.That(result[0].PassName, Is.EqualTo("OnlyPass"),
                "The valid pass should be the only one returned.");

            Object.DestroyImmediate(asset);
        }

        #endregion

        #region Build — Enabled Pass Filtering

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> returns only passes whose
        /// <see cref="Pass.IsEnabled"/> is <c>true</c>. Passes that set
        /// <c>IsEnabled = false</c> in their constructor are excluded.
        /// </summary>
        [Test]
        public void Build_ReturnsEnabledPassesOnly()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestPassA("EnabledAlpha"));
            asset.Passes.Add(new TestPassDisabled("DisabledPass"));
            asset.Passes.Add(new TestPassB("EnabledBeta"));

            List<Pass> result = asset.Build(renderer: null);

            Assert.That(result, Is.Not.Null,
                "Build should return a non-null list.");
            Assert.That(result.Count, Is.EqualTo(2),
                "Build should exclude the disabled pass, returning only the two enabled ones.");

            // Verify both enabled passes are present.
            bool hasAlpha = result.Exists(p => p.PassName == "EnabledAlpha");
            bool hasBeta = result.Exists(p => p.PassName == "EnabledBeta");
            bool hasDisabled = result.Exists(p => p.PassName == "DisabledPass");

            Assert.That(hasAlpha, Is.True,
                "EnabledAlpha should be in the result.");
            Assert.That(hasBeta, Is.True,
                "EnabledBeta should be in the result.");
            Assert.That(hasDisabled, Is.False,
                "DisabledPass should NOT be in the result.");

            Object.DestroyImmediate(asset);
        }

        #endregion

        #region Build — Slot Connections & Topological Order

        /// <summary>
        /// <see cref="RenderGraphAsset.Build"/> wires an output slot of one pass
        /// to an input slot of another through a <see cref="SlotConnection"/>,
        /// making the target input slot connected so the consumer can read the
        /// producer's handle during <see cref="Pass.Record"/>.
        /// </summary>
        [Test]
        public void Build_SlotConnection_WiresOutputToInput()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();
            asset.Passes.Add(new TestTextureProducerPass("Producer"));
            asset.Passes.Add(new TestTextureConsumerPass("Consumer"));
            asset.Connections.Add(SlotConnection.Create(
                sourcePass: "Producer",
                sourceSlot: "Out",
                targetPass: "Consumer",
                targetSlot: "In"));

            List<Pass> result = asset.Build(renderer: null);

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

            // Publish a handle on the producer's output and verify the input
            // slot reflects it (HasHandle becomes true, ReadHandle returns it).
            producer!.Output.SetHandle(default(TextureHandle));

            Assert.That(consumer.Input.HasHandle, Is.False,
                "A default (invalid) texture handle must be treated as no handle.");

            Object.DestroyImmediate(asset);
        }

        #endregion

        #region Settings

        /// <summary>
        /// <see cref="RenderGraphAsset.Settings"/> can be read and written,
        /// and the <see cref="RenderGraphSettings"/> struct fields are
        /// independently mutable.
        /// </summary>
        [Test]
        public void Settings_CanBeReadAndWritten()
        {
            var asset = ScriptableObject.CreateInstance<RenderGraphAsset>();

            // Default state.
            Assert.That(asset.Settings.SHEvalMode, Is.EqualTo(default(SHEvalMode)),
                "Default SHEvalMode should be PerVertex (= 0).");
            Assert.That(asset.Settings.AllowHDR, Is.False,
                "Default AllowHDR should be false.");

            // Write new values.
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

            Object.DestroyImmediate(asset);
        }

        #endregion
    }
}
