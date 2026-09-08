using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// Abstract base class for render passes in the HNRP custom render pipeline.
    /// Passes are serializable C# objects — not ScriptableObjects — that define a unit of
    /// rendering work within the render graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Since Unity 2022.3, <see cref="RenderGraphAsset"/> stores pass instances directly
    /// in a <c>[SerializeReference] List&lt;Pass&gt;</c>, so <see cref="Pass"/> subclasses
    /// are <c>[Serializable]</c> and their exposed parameters are <c>[SerializeField]</c>
    /// fields. The asset holds template instances; <see cref="CreateRuntimeClone"/> produces
    /// the per-camera runtime instance during build.
    /// </para>
    /// <para>Lifecycle order:</para>
    /// <list type="number">
    ///   <item><see cref="SetupSlots"/> — declare input/output slots (e.g. TextureSlot, ComputeBufferSlot)</item>
    ///   <item><see cref="PreRecord"/> — load resources using camera-specific context</item>
    ///   <item><see cref="Record"/> — record render commands into the render graph</item>
    ///   <item><see cref="Cleanup"/> — release resources held by this pass</item>
    /// </list>
    /// <para>
    /// Subclasses should be decorated with the <c>[Pass("Name")]</c> attribute
    /// for automatic discovery via reflection (Editor) or code generation (Player).
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class Pass
    {
        /// <summary>
        /// The instance name of this pass. Must be non-null and unique within a render graph.
        /// </summary>
        [SerializeField]
        private string m_PassName;

        /// <summary>
        /// Whether this pass is enabled. When <c>false</c>, the caller (e.g.
        /// <c>CameraRenderer</c>) skips <see cref="Record"/> for this pass. Default <c>true</c>.
        /// </summary>
        [SerializeField]
        private bool m_IsEnabled = true;

        /// <summary>
        /// Registry of this pass's slots declared in <see cref="SetupSlots"/>,
        /// keyed by <see cref="PassSlot.SlotName"/>.
        /// </summary>
        private readonly Dictionary<string, PassSlot> m_Slots = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Pass"/> class.
        /// Parameterless constructor used by Unity serialization
        /// (<c>[SerializeReference]</c> deserialization).
        /// </summary>
        protected Pass()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Pass"/> class.
        /// </summary>
        /// <param name="passName">
        /// The name of this pass. Must be non-null and unique within a render graph.
        /// </param>
        protected Pass(string passName)
        {
            m_PassName = passName;
        }

        /// <summary>
        /// Gets the name of this pass, set at construction time.
        /// </summary>
        public string PassName => m_PassName;

        /// <summary>
        /// Gets or sets a value indicating whether this pass is enabled.
        /// When <c>false</c>, the caller (e.g. <c>CameraRenderer</c>) skips
        /// <see cref="Record"/> for this pass.
        /// Default is <c>true</c>.
        /// </summary>
        public bool IsEnabled
        {
            get => m_IsEnabled;
            set => m_IsEnabled = value;
        }

        /// <summary>
        /// Copies all serialized parameters from another instance of the same concrete
        /// type. The instance name (<see cref="PassName"/>) is intentionally NOT copied —
        /// it is the identity of the pass within a render graph.
        /// </summary>
        /// <param name="source">
        /// The source pass. Must be the same concrete type as this instance.
        /// </param>
        public abstract void CopyFrom(Pass source);

        /// <summary>
        /// Creates a runtime clone of this template pass: instantiates a new instance
        /// via the parameterless constructor, copies serialized parameters through
        /// <see cref="CopyFrom"/>, and preserves the instance name
        /// (<see cref="PassName"/>, which <see cref="CopyFrom"/> intentionally does
        /// not copy). Used by <see cref="RenderGraphAsset.Build"/> so each camera
        /// owns an independent pass instance (ADR-001).
        /// </summary>
        /// <returns>A new pass of the same concrete type with copied parameters.</returns>
        internal Pass CreateRuntimeClone()
        {
            Pass clone = (Pass)Activator.CreateInstance(GetType());
            clone.CopyFrom(this);
            clone.m_PassName = m_PassName;
            return clone;
        }

        /// <summary>
        /// Declares input and output slots for this pass.
        /// Called once during setup, before <see cref="PreRecord"/>.
        /// </summary>
        /// <remarks>
        /// Slots define data dependencies — the render graph uses them to derive
        /// execution order automatically. Typical slot types: <c>TextureSlot</c>,
        /// <c>ComputeBufferSlot</c>, <c>RendererListSlot</c>.
        /// </remarks>
        public abstract void SetupSlots();

        /// <summary>
        /// Registers a slot declared in <see cref="SetupSlots"/>.
        /// Same-name registration overwrites the existing entry.
        /// </summary>
        /// <param name="slot">The slot to register. Must not be <c>null</c>.</param>
        protected void RegisterSlot(PassSlot slot)
        {
            m_Slots[slot.SlotName] = slot;
            slot.OwnerPass = this;
        }

        /// <summary>
        /// Gets a slot by name, or <c>null</c> if no slot with that name is registered.
        /// </summary>
        /// <param name="name">The name of the slot to look up.</param>
        /// <returns>The registered <see cref="PassSlot"/>, or <c>null</c> if not found.</returns>
        public PassSlot GetSlot(string name) => m_Slots.TryGetValue(name, out var slot) ? slot : null;

        /// <summary>
        /// Connects this pass's output slot (<paramref name="sourceSlotName"/>) to
        /// target pass's input slot (<paramref name="targetSlotName"/>).
        /// </summary>
        /// <param name="sourceSlotName">The name of this pass's output slot.</param>
        /// <param name="target">The target pass whose input slot receives the connection.</param>
        /// <param name="targetSlotName">The name of the target pass's input slot.</param>
        /// <returns>
        /// <c>true</c> if the connection was established; <c>false</c> if either slot
        /// is missing, directions don't match (output→input required), or the slots
        /// carry different resource types.
        /// </returns>
        public bool TryConnect(string sourceSlotName, Pass target, string targetSlotName)
        {
            if (!m_Slots.TryGetValue(sourceSlotName, out var sourceSlot)) return false;
            if (!target.m_Slots.TryGetValue(targetSlotName, out var targetSlot)) return false;
            if (sourceSlot.Direction != SlotDirection.Output) return false;
            if (targetSlot.Direction != SlotDirection.Input) return false;
            try
            {
                sourceSlot.Connect(targetSlot);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Resets all output slot handles on this pass. Call at the start of each frame
        /// before Record so stale handles from a previous frame cannot be read.
        /// </summary>
        public void ResetSlotHandles()
        {
            foreach (PassSlot slot in m_Slots.Values)
            {
                if (slot.Direction == SlotDirection.Output)
                {
                    slot.ResetHandle();
                }
            }
        }

        /// <summary>
        /// Initializes this pass with camera-specific rendering context.
        /// Use this method to load shaders, materials, and other resources.
        /// </summary>
        /// <param name="context">
        /// The camera rendering context providing camera-specific data.
        /// </param>
        public abstract void PreRecord(RenderGraphAsset template, CameraContext context);

        /// <summary>
        /// Records render commands into the render graph.
        /// Called only when <see cref="IsEnabled"/> is <c>true</c>.
        /// </summary>
        /// <param name="renderGraph">
        /// The render graph to record commands into. Output slots create resources
        /// via <c>renderGraph.CreateTexture</c> etc.; input slots read from connected outputs.
        /// </param>
        public abstract void Record(RenderGraph renderGraph);

        /// <summary>
        /// Releases resources held by this pass.
        /// Default implementation is a no-op. Override to release
        /// materials, compute buffers, or other disposable resources.
        /// </summary>
        public virtual void Cleanup()
        {
        }
    }
}
