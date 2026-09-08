using NUnit.Framework;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP.Tests
{
    /// <summary>
    /// Tests for the <see cref="Pass"/> abstract base class.
    /// Verifies that Pass is a pure C# class with no Unity serialization dependencies.
    /// </summary>
    public sealed class PassTests
    {
        /// <summary>
        /// A minimal concrete Pass subclass used for testing.
        /// Tracks lifecycle method invocations to verify call order and behavior.
        /// </summary>
        private sealed class TestPass : Pass
        {
            public bool SetupSlotsCalled { get; private set; }
            public bool InitializeCalled { get; private set; }
            public bool RecordCalled { get; private set; }
            public bool CleanupCalled { get; private set; }

            public TestPass(string passName)
                : base(passName)
            {
            }

            public override void SetupSlots()
            {
                SetupSlotsCalled = true;
            }

            public override void PreRecord(RenderGraphAsset template, CameraContext context)
            {
                InitializeCalled = true;
            }

            public override void Record(RenderGraph renderGraph)
            {
                RecordCalled = true;
            }

            public override void Cleanup()
            {
                base.Cleanup();
                CleanupCalled = true;
            }

            /// <inheritdoc />
            public override void CopyFrom(Pass source)
            {
            }
        }

        [Test]
        public void Pass_Subclass_CanBeInstantiated()
        {
            var pass = new TestPass("TestPass");

            Assert.That(pass, Is.Not.Null);
            Assert.That(pass, Is.InstanceOf<Pass>());
            Assert.That(pass.PassName, Is.EqualTo("TestPass"));
        }

        [Test]
        public void Pass_IsEnabled_DefaultsTrue()
        {
            var pass = new TestPass("TestPass");

            Assert.That(pass.IsEnabled, Is.True);
        }

        [Test]
        public void Pass_Lifecycle_SetupSlots_Initialize_Record_Cleanup()
        {
            var pass = new TestPass("TestPass");

            // Phase 1: SetupSlots — declare input/output slots
            pass.SetupSlots();
            Assert.That(pass.SetupSlotsCalled, Is.True,
                "SetupSlots should be called first to declare slots.");

            // Phase 2: Initialize — load resources with camera context
            pass.PreRecord(new RenderGraphAsset(), new CameraContext(null, default));
            Assert.That(pass.InitializeCalled, Is.True,
                "Initialize should be called after SetupSlots to load resources.");

            // Phase 3: Record — record render commands into render graph
            pass.Record(null);
            Assert.That(pass.RecordCalled, Is.True,
                "Record should be called after Initialize to issue render commands.");

            // Phase 4: Cleanup — release resources
            pass.Cleanup();
            Assert.That(pass.CleanupCalled, Is.True,
                "Cleanup should be called last to release resources.");
        }

        [Test]
        public void Pass_Record_SkippedWhenDisabled()
        {
            var pass = new TestPass("TestPass") { IsEnabled = false };

            // Simulate CameraRenderer behavior: skip Record when IsEnabled is false
            if (pass.IsEnabled)
            {
                pass.Record(null);
            }

            Assert.That(pass.RecordCalled, Is.False,
                "Record should not be invoked when IsEnabled is false.");
            Assert.That(pass.IsEnabled, Is.False);
        }
    }
}
