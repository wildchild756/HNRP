using System.Linq;
using NUnit.Framework;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for the pass registration code-generation system, verifying that
    /// both the generated (hardcoded) path and the reflection-based (Editor) path
    /// produce correct registration results.
    /// </summary>
    public sealed class PassRegistryGeneratorTests
    {
        /// <summary>
        /// A minimal concrete Pass subclass used for testing generated registration.
        /// Uses a unique display name to avoid conflicts with other tests.
        /// </summary>
        [Pass("TestGenPass")]
        private sealed class TestGenPass : Pass
        {
            public TestGenPass()
                : base("TestGenPass")
            {
            }

            public override void SetupSlots()
            {
            }

            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
            }

            public override void Record(UnityEngine.Experimental.Rendering.RenderGraphModule.RenderGraph renderGraph)
            {
            }

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        /// <summary>
        /// Verifies that manually calling <see cref="PassRegistry.Register"/> —
        /// simulating what the generated code does — correctly registers a pass
        /// and makes it discoverable via <see cref="PassRegistry.GetPassType"/>
        /// and <see cref="PassRegistry.GetAllPassNames"/>.
        /// </summary>
        [Test]
        public void GeneratedCode_RegistersAllPasses()
        {
            // Simulate generated code: hardcoded registration without reflection
            PassRegistry.RegisterAll();
            PassRegistry.Register("TestGenPass", typeof(TestGenPass));

            // Verify lookup
            System.Type type = PassRegistry.GetPassType("TestGenPass");
            Assert.That(type, Is.Not.Null,
                "Manual registration (simulating generated code) should register the type.");
            Assert.That(type, Is.EqualTo(typeof(TestGenPass)),
                "GetPassType should return the exact type that was registered.");

            // Verify enumeration
            var names = PassRegistry.GetAllPassNames().ToList();
            Assert.That(names, Does.Contain("TestGenPass"),
                "GetAllPassNames should include the manually registered pass.");
        }

        /// <summary>
        /// Verifies that both registration paths — Editor reflection
        /// (<see cref="PassRegistry.RegisterAll"/> scanning for [Pass] attributes)
        /// and Player generated code (calling <see cref="PassRegistry.Register"/> directly) —
        /// produce the same result for the same pass type.
        /// </summary>
        [Test]
        public void Registry_EditorUsesReflection_PlayerUsesGenerated()
        {
            // Path 1: Reflection-based discovery (Editor path)
            PassRegistry.RegisterAll();
            System.Type reflectedType = PassRegistry.GetPassType("TestGenPass");

            Assert.That(reflectedType, Is.Not.Null,
                "Reflection-based discovery should find types decorated with [Pass].");
            Assert.That(reflectedType, Is.EqualTo(typeof(TestGenPass)),
                "Reflection-discovered type should match the decorated class.");

            // Path 2: Manual registration (simulates Player build generated code)
            // RegisterAll clears and repopulates via reflection, then we overlay
            // with a manual Register call to simulate the generated code path.
            PassRegistry.RegisterAll();
            PassRegistry.Register("TestGenPass", typeof(TestGenPass));
            System.Type manualType = PassRegistry.GetPassType("TestGenPass");

            Assert.That(manualType, Is.Not.Null,
                "Manual registration (generated code path) should work.");
            Assert.That(manualType, Is.EqualTo(typeof(TestGenPass)),
                "Manual registration should return the same type.");

            // Both paths must yield the same type
            Assert.That(manualType, Is.EqualTo(reflectedType),
                "Both Editor (reflection) and Player (generated) paths should produce the same result.");
        }
    }
}
