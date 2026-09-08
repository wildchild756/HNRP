// <copyright file="PassSlot.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace HN.HNRP
{
    /// <summary>
    /// Defines the direction of a <see cref="PassSlot"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><see cref="Input"/> — reads the resource handle from a connected output slot.</item>
    ///   <item><see cref="Output"/> — creates a resource handle that input slots can read.</item>
    /// </list>
    /// </remarks>
    public enum SlotDirection
    {
        /// <summary>
        /// Input slot: reads from a connected output's handle.
        /// </summary>
        Input,

        /// <summary>
        /// Output slot: creates a resource handle for downstream inputs.
        /// </summary>
        Output,
    }

    /// <summary>
    /// Abstract base class for name-based render pass slots.
    /// Replaces the legacy index-based slot system with a name-driven model.
    /// </summary>
    /// <remarks>
    /// <para><b>Connection model:</b></para>
    /// <list type="bullet">
    ///   <item>Output slots call <see cref="PassSlot{T}.SetHandle"/> to publish a resource handle.</item>
    ///   <item>Output slots call <see cref="Connect"/> to link an input slot.</item>
    ///   <item>Input slots call <see cref="PassSlot{T}.ReadHandle"/> to retrieve the connected output's handle.</item>
    /// </list>
    /// <para>
    /// This class is pure C# — no <c>ScriptableObject</c> inheritance and no Unity serialization
    /// attributes. It is designed to be lightweight and testable outside the Unity Editor.
    /// </para>
    /// </remarks>
    public abstract class PassSlot
    {
        /// <summary>
        /// Gets the name of this slot. Must be non-empty and unique within a pass.
        /// </summary>
        public string SlotName { get; }

        /// <summary>
        /// Gets the direction of this slot — <see cref="SlotDirection.Input"/> or
        /// <see cref="SlotDirection.Output"/>.
        /// </summary>
        public SlotDirection Direction { get; }

        /// <summary>
        /// Gets a value indicating whether this slot is connected.
        /// For input slots, <c>true</c> after a successful <see cref="Connect"/> call.
        /// For output slots, always <c>false</c> (an output can drive multiple inputs
        /// and does not track its own connection state).
        /// </summary>
        public bool IsConnected { get; protected set; }

        /// <summary>
        /// For input slots: the output slot this input is connected to.
        /// For output slots: always <c>null</c>.
        /// </summary>
        protected PassSlot? connectedOutput;

        /// <summary>
        /// The <see cref="Pass"/> that owns this slot.
        /// Set by <see cref="Pass.RegisterSlot"/>; used to derive pass-level
        /// dependencies from connections at build time.
        /// </summary>
        public Pass OwnerPass { get; internal set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PassSlot"/> class.
        /// </summary>
        /// <param name="slotName">
        /// The name of the slot. Must be non-null and non-empty (whitespace-only is rejected).
        /// </param>
        /// <param name="direction">
        /// Whether this slot is an <see cref="SlotDirection.Input"/> or
        /// <see cref="SlotDirection.Output"/>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="slotName"/> is <c>null</c>, empty, or whitespace-only.
        /// </exception>
        protected PassSlot(string slotName, SlotDirection direction)
        {
            if (string.IsNullOrWhiteSpace(slotName))
            {
                throw new ArgumentException(
                    "Slot name must not be null, empty, or whitespace-only.",
                    nameof(slotName));
            }

            SlotName = slotName;
            Direction = direction;
        }

        /// <summary>
        /// Connects this output slot to the given input slot.
        /// After connection, the input slot can call <see cref="PassSlot{T}.ReadHandle"/>
        /// to retrieve this output's resource handle.
        /// </summary>
        /// <param name="input">
        /// The input slot to connect to. Must have <see cref="SlotDirection.Input"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="input"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when this slot is not an output, or <paramref name="input"/> is not an input.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the output and input slots carry different resource types.
        /// </exception>
        public virtual void Connect(PassSlot input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (Direction != SlotDirection.Output)
            {
                throw new InvalidOperationException(
                    "Only output slots can initiate a connection.");
            }

            if (input.Direction != SlotDirection.Input)
            {
                throw new InvalidOperationException(
                    "Can only connect an output slot to an input slot.");
            }

            if (!CanConnectTo(input))
            {
                throw new ArgumentException(
                    $"Slot type mismatch: {GetType().Name} cannot connect to {input.GetType().Name}. " +
                    "Output and input slots must carry the same resource type.");
            }

            input.connectedOutput = this;
            input.IsConnected = true;
        }

        /// <summary>
        /// Determines whether this output slot can connect to the given input slot.
        /// The base implementation allows any connection; <see cref="PassSlot{T}"/>
        /// overrides it to require matching resource types.
        /// </summary>
        /// <param name="input">The input slot to validate against.</param>
        /// <returns><c>true</c> when the connection is type-compatible.</returns>
        protected virtual bool CanConnectTo(PassSlot input) => true;

        /// <summary>
        /// Clears this slot's stored handle. Call at the start of each frame
        /// before <c>Record</c> so a stale handle from a previous frame cannot be read.
        /// </summary>
        public abstract void ResetHandle();
    }

    /// <summary>
    /// A strongly-typed <see cref="PassSlot"/> that stores its resource handle
    /// directly in a value-type field, eliminating per-frame boxing allocations.
    /// </summary>
    /// <typeparam name="T">
    /// The render graph resource handle struct this slot carries
    /// (e.g. <see cref="TextureHandle"/>, <see cref="ComputeBufferHandle"/>,
    /// <see cref="RendererListHandle"/>).
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// <b>Zero-allocation:</b> the handle is stored directly in a typed field,
    /// so <see cref="SetHandle"/> never boxes the struct. This keeps the
    /// render loop allocation-free.
    /// </para>
    /// <para>
    /// <b><see cref="HasHandle"/> semantics:</b> for an output slot, <c>true</c>
    /// only after <see cref="SetHandle"/> was called <i>and</i> the stored value
    /// passes <see cref="IsValueValid"/>. For an input slot, <c>true</c> when the
    /// connected output currently holds a valid handle. A default (invalid)
    /// handle — e.g. <c>default(TextureHandle)</c> — is treated as "no handle".
    /// Passes use this to decide whether to consume an upstream resource or
    /// allocate their own.
    /// </para>
    /// <para>
    /// <b><see cref="ResetHandle"/>:</b> call at the start of each frame
    /// before <c>Record</c> so stale handles from a previous frame cannot be read.
    /// </para>
    /// </remarks>
    public class PassSlot<T> : PassSlot
    {
        /// <summary>
        /// The value-type resource handle stored directly in this slot.
        /// </summary>
        private T m_Value;

        /// <summary>
        /// Whether <see cref="SetHandle"/> has been called since the last
        /// <see cref="ResetHandle"/>.
        /// </summary>
        private bool m_HasValue;

        /// <summary>
        /// Gets a value indicating whether this slot currently holds a valid
        /// resource handle. For an output slot, <c>true</c> only after
        /// <see cref="SetHandle"/> was called with a value that passes
        /// <see cref="IsValueValid"/>. For an input slot, <c>true</c> when the
        /// connected output currently holds a valid handle.
        /// </summary>
        public bool HasHandle
        {
            get
            {
                if (Direction == SlotDirection.Output)
                {
                    return m_HasValue && IsValueValid(m_Value);
                }

                // Input: reflect the connected output's stored handle.
                if (connectedOutput is PassSlot<T> output)
                {
                    return output.m_HasValue && IsValueValid(output.m_Value);
                }

                return false;
            }
        }

        /// <summary>
        /// Validates the stored handle value. The base implementation accepts any
        /// value; concrete slots delegate to the Unity handle type's validity check
        /// (e.g. <see cref="TextureHandle.IsValid"/>).
        /// </summary>
        /// <param name="value">The handle value to validate.</param>
        /// <returns><c>true</c> when the handle is valid.</returns>
        protected virtual bool IsValueValid(T value) => true;

        /// <summary>
        /// Initializes a new instance of the <see cref="PassSlot{T}"/> class.
        /// </summary>
        /// <param name="slotName">The name of the slot.</param>
        /// <param name="direction">Whether this is an input or output slot.</param>
        public PassSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <summary>
        /// Sets the resource handle for this slot. Output slots use this to publish
        /// the real render graph handle that connected input slots read via
        /// <see cref="ReadHandle"/>. The value is stored directly (zero allocation).
        /// </summary>
        /// <param name="value">The real render graph resource handle.</param>
        public void SetHandle(T value)
        {
            m_Value = value;
            m_HasValue = true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Resets the stored value to <c>default</c> and clears the has-value flag.
        /// </remarks>
        public override void ResetHandle()
        {
            m_Value = default;
            m_HasValue = false;
        }

        /// <summary>
        /// Reads the resource handle associated with this slot.
        /// </summary>
        /// <returns>
        /// For an output slot: its own stored handle.
        /// For a connected input slot: the connected output's handle.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when this is an input slot that is not yet connected.
        /// </exception>
        public T ReadHandle()
        {
            if (Direction == SlotDirection.Output)
            {
                return m_Value;
            }

            // Direction is Input
            if (!IsConnected)
            {
                throw new InvalidOperationException(
                    "Input slot is not connected to an output slot. " +
                    "Call Connect() on the output slot first.");
            }

            return ((PassSlot<T>)connectedOutput).m_Value;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Requires the input slot to carry the same resource type
        /// (i.e. <see cref="PassSlot{T}"/> with the same <typeparamref name="T"/>).
        /// </remarks>
        protected override bool CanConnectTo(PassSlot input) => input is PassSlot<T>;
    }

    #region Concrete Slot Types

    /// <summary>
    /// A <see cref="PassSlot{T}"/> that represents a texture resource.
    /// </summary>
    public class TextureSlot : PassSlot<TextureHandle>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TextureSlot"/> class.
        /// </summary>
        /// <param name="slotName">The name of the texture slot.</param>
        /// <param name="direction">Whether this is an input or output slot.</param>
        public TextureSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// A <see cref="TextureHandle"/> is valid only when it references an
        /// actual render graph texture — a default handle is not valid.
        /// </remarks>
        protected override bool IsValueValid(TextureHandle value) => value.IsValid();
    }

    /// <summary>
    /// A <see cref="PassSlot{T}"/> that represents a compute buffer resource.
    /// </summary>
    public class ComputeBufferSlot : PassSlot<ComputeBufferHandle>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ComputeBufferSlot"/> class.
        /// </summary>
        /// <param name="slotName">The name of the compute buffer slot.</param>
        /// <param name="direction">Whether this is an input or output slot.</param>
        public ComputeBufferSlot(string slotName, SlotDirection direction)
            : base(slotName, direction)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// A <see cref="ComputeBufferHandle"/> is valid only when it references an
        /// actual render graph buffer — a default handle is not valid.
        /// </remarks>
        protected override bool IsValueValid(ComputeBufferHandle value) => value.IsValid();
    }

    #endregion
}
