// <copyright file="CameraRenderer.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// Per-camera independent renderer.
    /// Each <see cref="CameraRenderer"/> owns a runtime <see cref="List{Pass}"/>,
    /// a <see cref="CameraContext"/>, and a reference to the current
    /// <see cref="RenderGraphAsset"/> template. It is responsible for building
    /// passes from a template, managing pass lifecycle, and executing the render
    /// graph for a single camera.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture note</b> (ADR-002, ADR-011):
    /// <see cref="RenderGraphAsset"/> is the static blueprint (ScriptableObject);
    /// <see cref="CameraRenderer"/> owns the runtime pass list per camera.
    /// </para>
    /// <para>
    /// The render loop calls <see cref="Build"/> (or <see cref="Reset"/>) to
    /// populate the pass list, then <see cref="Render"/> each frame to execute
    /// passes in order.
    /// </para>
    /// </remarks>
    /// <seealso cref="Pass"/>
    /// <seealso cref="CameraContext"/>
    /// <seealso cref="RenderGraphAsset"/>
    public class CameraRenderer
    {
        /// <summary>
        /// The ordered list of runtime <see cref="Pass"/> instances owned by this renderer.
        /// Populated by <see cref="Build"/> or <see cref="Reset"/>.
        /// </summary>
        public List<Pass> Passes { get; private set; } = new();

        /// <summary>
        /// The per-camera rendering context that passes reference during execution.
        /// </summary>
        public CameraContext Context { get; set; }

        /// <summary>
        /// The current <see cref="RenderGraphAsset"/> template. Null until the first
        /// call to <see cref="Build"/> or <see cref="Reset"/>.
        /// </summary>
        public RenderGraphAsset CurrentTemplate { get; private set; }

        /// <summary>
        /// Internal storage for slot connections added via <see cref="Connect"/>.
        /// These are wired during <see cref="Build"/> or <see cref="Reset"/>.
        /// </summary>
        private readonly List<SlotConnection> m_ManualConnections = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="CameraRenderer"/> class.
        /// </summary>
        /// <param name="context">
        /// The per-camera rendering context. May be <c>null</c> if the caller will
        /// set it later or supply it via <see cref="Render"/>.
        /// </param>
        public CameraRenderer(CameraContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Builds the runtime pass list from a <see cref="RenderGraphAsset"/> template.
        /// Calls <see cref="RenderGraphAsset.Build"/> which instantiates passes from
        /// definitions, wires connections, and filters to enabled passes only.
        /// </summary>
        /// <param name="template">
        /// The render graph template asset. Must not be <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="template"/> is <c>null</c>.
        /// </exception>
        public void Build(RenderGraphAsset template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            CurrentTemplate = template;
            Passes = template.Build(this) ?? new List<Pass>();

            WireManualConnections();
        }

        /// <summary>
        /// Creates a new pass of type <typeparamref name="T"/> with the given instance
        /// name and appends it to the pass list.
        /// </summary>
        /// <typeparam name="T">
        /// The concrete <see cref="Pass"/> subclass to instantiate. Must have a
        /// public constructor that accepts a single <see cref="string"/> argument
        /// (the pass name), as well as a parameterless constructor for the
        /// <c>new()</c> constraint.
        /// </typeparam>
        /// <param name="name">The instance name for the new pass.</param>
        /// <returns>The newly created pass instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="name"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <typeparamref name="T"/> cannot be instantiated with a
        /// string constructor.
        /// </exception>
        public T AddPass<T>(string name)
            where T : Pass
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            // Instantiate via the string constructor, matching how
            // RenderGraphAsset.InstantiatePass works. The new() constraint
            // provides compile-time type safety but the string constructor
            // is the expected construction path.
            Pass instance;
            try
            {
                instance = (Pass)Activator.CreateInstance(typeof(T), name);
            }
            catch (MissingMethodException ex)
            {
                throw new InvalidOperationException(
                    $"Pass type '{typeof(T).FullName}' does not have a public constructor " +
                    $"that accepts a single string argument. All Pass subclasses must " +
                    $"implement a constructor of the form: public {typeof(T).Name}(string name) : base(name) {{ }}",
                    ex);
            }

            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate pass of type '{typeof(T).FullName}'.");
            }

            Passes.Add(instance);
            return (T)instance;
        }

        /// <summary>
        /// Removes a pass from the runtime list by its instance name.
        /// If no pass with the given name exists, this method is a no-op.
        /// </summary>
        /// <param name="name">The instance name of the pass to remove.</param>
        public void RemovePass(string name)
        {
            Passes.RemoveAll(p => p.PassName == name);
        }

        /// <summary>
        /// Finds a pass of type <typeparamref name="T"/> by its instance name.
        /// </summary>
        /// <typeparam name="T">The expected pass type.</typeparam>
        /// <param name="name">The instance name of the pass to find.</param>
        /// <returns>
        /// The matching <see cref="Pass"/> cast to <typeparamref name="T"/>,
        /// or <c>null</c> if no pass with the given name exists.
        /// </returns>
        public T FindPass<T>(string name)
            where T : Pass
        {
            foreach (Pass pass in Passes)
            {
                if (pass.PassName == name && pass is T typedPass)
                {
                    return typedPass;
                }
            }

            return null;
        }

        /// <summary>
        /// Toggles whether a pass is enabled.
        /// When disabled, the pass is skipped during <see cref="Render"/>.
        /// </summary>
        /// <param name="name">The instance name of the pass.</param>
        /// <param name="enabled">The desired enabled state.</param>
        public void SetPassEnabled(string name, bool enabled)
        {
            foreach (Pass pass in Passes)
            {
                if (pass.PassName == name)
                {
                    pass.IsEnabled = enabled;
                    return;
                }
            }
        }

        /// <summary>
        /// Connects an output slot of one pass to an input slot of another pass
        /// by name. Connections are stored and wired during the next
        /// <see cref="Build"/> or <see cref="Reset"/> call.
        /// </summary>
        /// <param name="sourcePass">The instance name of the source pass.</param>
        /// <param name="sourceSlot">The name of the output slot on the source pass.</param>
        /// <param name="targetPass">The instance name of the target pass.</param>
        /// <param name="targetSlot">The name of the input slot on the target pass.</param>
        /// <remarks>
        /// <para>
        /// Connections are resolved at build time. If passes expose named slots
        /// (a future API), the actual data-flow wiring is performed automatically.
        /// Currently the connection record is stored for forward compatibility.
        /// </para>
        /// </remarks>
        public void Connect(
            string sourcePass,
            string sourceSlot,
            string targetPass,
            string targetSlot)
        {
            m_ManualConnections.Add(
                SlotConnection.Create(sourcePass, sourceSlot, targetPass, targetSlot));
        }

        /// <summary>
        /// Rebuilds the pass list from a new template, resetting all runtime state.
        /// This clears manually added passes and re-invokes
        /// <see cref="RenderGraphAsset.Build"/>.
        /// </summary>
        /// <param name="newTemplate">
        /// The new render graph template asset. Must not be <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="newTemplate"/> is <c>null</c>.
        /// </exception>
        public void Reset(RenderGraphAsset newTemplate)
        {
            if (newTemplate == null)
            {
                throw new ArgumentNullException(nameof(newTemplate));
            }

            Passes.Clear();
            m_ManualConnections.Clear();
            Build(newTemplate);
        }

        /// <summary>
        /// Executes all enabled passes for the current frame.
        /// </summary>
        /// <param name="renderGraph">The render graph to record commands into.</param>
        /// <param name="context">
        /// The scriptable render context for the current frame. Updates
        /// <see cref="CameraContext.Context"/> on <see cref="Context"/>.
        /// </param>
        /// <remarks>
        /// <para><b>Execution order per pass:</b></para>
        /// <list type="number">
        ///   <item><see cref="Pass.ResetSlotHandles"/> — clear stale output slot handles from the previous frame</item>
        ///   <item><see cref="Pass.PreRecord"/> — load resources using camera context</item>
        ///   <item><see cref="Pass.Record"/> — record render graph commands</item>
        /// </list>
        /// <para>
        /// <see cref="Pass.SetupSlots"/> is intentionally <b>not</b> called here:
        /// slots are declared once during <see cref="Build(RenderGraphAsset)"/>
        /// (via <c>RenderGraphAsset.Build</c>) so that slot connections established
        /// at build time reference the same instances used every frame.
        /// </para>
        /// <para>
        /// After all enabled passes execute, <see cref="Pass.Cleanup"/> is called
        /// on <b>every</b> pass (including disabled ones) to release any held resources.
        /// </para>
        /// </remarks>
        public void Render(RenderGraph renderGraph, ScriptableRenderContext context)
        {
            // Update the camera context with the current frame's ScriptableRenderContext.
            if (Context != null)
            {
                Context.Context = context;
            }

            // ── Phase 1–3: Execute each enabled pass ──
            foreach (Pass pass in Passes)
            {
                if (!pass.IsEnabled)
                {
                    continue;
                }

                pass.ResetSlotHandles();
                pass.PreRecord(CurrentTemplate, Context);
                pass.Record(renderGraph);
            }

            // ── Phase 4: Cleanup all passes ──
            foreach (Pass pass in Passes)
            {
                pass.Cleanup();
            }
        }

        /// <summary>
        /// Wires the manually added <see cref="SlotConnection"/> entries.
        /// Resolves pass names to instances and connects their slots.
        /// </summary>
        /// <remarks>
        /// Currently a forward-looking implementation — the actual per-slot
        /// wiring depends on <see cref="Pass"/> exposing named-slot access
        /// (see Todo 14 in <c>RenderGraphAsset.cs</c>). Until that API exists,
        /// connections are validated for name resolution only.
        /// </remarks>
        private void WireManualConnections()
        {
            foreach (SlotConnection conn in m_ManualConnections)
            {
                if (!conn.IsValid())
                {
                    continue;
                }

                Pass source = FindPass<Pass>(conn.SourcePass);
                Pass target = FindPass<Pass>(conn.TargetPass);

                if (source == null || target == null)
                {
                    continue;
                }

                // Future: resolve named slots here:
                //   PassSlot sourceSlot = source.GetOutputSlot(conn.SourceSlot);
                //   PassSlot targetSlot = target.GetInputSlot(conn.TargetSlot);
                //   sourceSlot.Connect(targetSlot);
            }
        }
    }
}
