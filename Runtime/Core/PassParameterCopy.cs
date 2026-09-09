// <copyright file="PassParameterCopy.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Reflection;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// Pass 参数拷贝工具：在「参数缓存 Pass」与「运行时 Pass」之间拷贝可序列化
    /// 参数字段。用于渲染图资源的参数覆盖注入（编辑器改参数 → 运行时 Build 生效）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参数缓存（见 <see cref="RenderGraphAsset.PassParameterCache"/>）是<b>同类型</b>
    /// 的另一个 <see cref="Pass"/> 实例，仅承载参数值，不参与渲染。注入时把缓存值
    /// 拷入模板代码创建的运行时 pass 实例，实现「模板默认 → 编辑器保存值」的覆盖。
    /// </para>
    /// <para>
    /// 只拷贝<b>可序列化字段</b>（public 字段或带 <c>[SerializeField]</c> 的字段），
    /// 跳过 readonly/const 与 <see cref="PassSlot"/> 字段。遍历范围覆盖从
    /// <see cref="Pass"/> 基类到具体子类的整条类型链，因此参数缓存中保存的
    /// <c>isEnabled</c>（面板 Header 开关）也会注入运行时 pass；<c>passName</c>
    /// 为结构身份字段（模板代码决定），不参与覆盖。本类保持 Pass 自身简洁 ——
    /// 不向 <see cref="Pass"/> 引入任何拷贝函数。
    /// </para>
    /// </remarks>
    public static class PassParameterCopy
    {
        /// <summary>
        /// 把 <paramref name="source"/> 的可序列化字段值拷入
        /// <paramref name="target"/>。两个实例必须同具体类型。
        /// </summary>
        /// <param name="source">字段来源实例（通常为参数缓存 Pass）。</param>
        /// <param name="target">字段目标实例（通常为运行时 Pass）。</param>
        public static void CopyParameters(Pass source, Pass target)
        {
            if (source == null || target == null)
            {
                return;
            }

            if (source.GetType() != target.GetType())
            {
                Debug.LogWarning(
                    $"PassParameterCopy.CopyParameters: 类型不一致，跳过拷贝。" +
                    $"(source='{source.GetType().Name}', target='{target.GetType().Name}')。");
                return;
            }

            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            // 从具体子类遍历到 Pass 基类（含基类的 [SerializeField] isEnabled）。
            for (Type type = target.GetType();
                 type != null && typeof(Pass).IsAssignableFrom(type);
                 type = type.BaseType)
            {
                FieldInfo[] fields = type.GetFields(flags);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    // 只拷贝可序列化参数：public 字段或带 [SerializeField] 的私有字段。
                    bool serialized = field.IsPublic
                        || field.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized)
                    {
                        continue;
                    }

                    // Slot 是运行时连接状态，不属于参数。
                    if (typeof(PassSlot).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    // passName 是结构身份（模板代码决定），不参与覆盖。
                    if (field.Name == "passName")
                    {
                        continue;
                    }

                    field.SetValue(target, field.GetValue(source));
                }
            }
        }
    }
}
