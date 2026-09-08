using System;
using System.Linq;
using NUnit.Framework;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for <see cref="PassRegistry"/>, verifying reflection-based pass discovery
    /// and name-based lookup.
    /// </summary>
    public sealed class PassRegistryTests
    {
        /// <summary>
        /// A minimal concrete Pass subclass used for testing registry discovery.
        /// </summary>
        [Pass("TestPass")]
        private sealed class TestPass : Pass
        {
            public TestPass()
                : base("TestPass")
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

        [Test]
        public void RegisterAll_DiscoversPassTypes()
        {
            PassRegistry.RegisterAll();

            Type type = PassRegistry.GetPassType("TestPass");

            Assert.That(type, Is.Not.Null,
                "RegisterAll should discover types decorated with [Pass].");
            Assert.That(type, Is.EqualTo(typeof(TestPass)),
                "Registered type should be the exact decorated class.");
        }

        [Test]
        public void GetPassType_ByName_ReturnsCorrectType()
        {
            PassRegistry.RegisterAll();

            Type type = PassRegistry.GetPassType("TestPass");

            Assert.That(type, Is.Not.Null,
                "Exact name lookup should return a non-null Type.");
            Assert.That(type, Is.EqualTo(typeof(TestPass)),
                "The returned Type should match the decorated class.");
        }

        [Test]
        public void GetPassType_UnknownName_ReturnsNull()
        {
            PassRegistry.RegisterAll();

            Type type = PassRegistry.GetPassType("NonExistentPass");

            Assert.That(type, Is.Null,
                "Unknown names should return null.");
        }

        [Test]
        public void GetPassType_NullName_ReturnsNull()
        {
            PassRegistry.RegisterAll();

            Type type = PassRegistry.GetPassType(null);

            Assert.That(type, Is.Null,
                "Null name should return null without throwing.");
        }

        [Test]
        public void GetAllPassNames_ReturnsAll()
        {
            PassRegistry.RegisterAll();

            var names = PassRegistry.GetAllPassNames().ToList();

            Assert.That(names, Is.Not.Null,
                "GetAllPassNames should return a non-null collection.");
            Assert.That(names, Does.Contain("TestPass"),
                "GetAllPassNames should contain the registered pass name.");
        }
    }
}
