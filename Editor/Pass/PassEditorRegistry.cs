// <copyright file="PassEditorRegistry.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// 注册表：把 <see cref="Pass"/> 具体类型映射到对应的 <see cref="PassEditor"/> 单例。
    /// 通过 <see cref="PassEditorAttribute"/> 标注的绑定关系，用 <see cref="TypeCache"/> 扫描发现。
    /// </summary>
    /// <remarks>
    /// <see cref="Pass"/> 是纯 C# 序列化对象（非 <see cref="UnityEngine.Object"/>），
    /// 不能使用 Unity 的 <c>[CustomEditor]</c> + <c>Editor.CreateEditor</c> 机制，
    /// 因此用自定义 attribute + 静态注册表完成类型到编辑器的映射。
    /// 编辑器实例保持无状态，可安全缓存为单例。
    /// </remarks>
    public static class PassEditorRegistry
    {
        /// <summary>
        /// Pass 类型 → 编辑器实例的映射。首个注册生效。
        /// </summary>
        private static readonly Dictionary<Type, PassEditor> editors = new();

        /// <summary>
        /// 静态构造：注册全部编辑器。
        /// </summary>
        static PassEditorRegistry()
        {
            RegisterAll();
        }

        /// <summary>
        /// 获取指定 Pass 类型对应的编辑器实例；未绑定时返回默认编辑器
        /// （遍历绘制全部序列化字段）。
        /// </summary>
        /// <param name="passType">Pass 具体类型。不能为 null。</param>
        /// <returns>对应的 <see cref="PassEditor"/> 实例，永不为 null。</returns>
        public static PassEditor GetEditor(Type passType)
        {
            if (passType == null)
            {
                return DefaultPassEditor.Instance;
            }

            return editors.TryGetValue(passType, out PassEditor editor)
                ? editor
                : DefaultPassEditor.Instance;
        }

        /// <summary>
        /// 扫描全部 <see cref="PassEditor"/> 派生类型并注册绑定关系。
        /// </summary>
        private static void RegisterAll()
        {
            editors.Clear();

            foreach (Type type in TypeCache.GetTypesDerivedFrom<PassEditor>())
            {
                if (type.IsAbstract)
                {
                    continue;
                }

                PassEditorAttribute attr =
                    type.GetCustomAttribute<PassEditorAttribute>(inherit: false);
                if (attr == null || attr.PassType == null)
                {
                    continue;
                }

                if (!typeof(Pass).IsAssignableFrom(attr.PassType))
                {
                    continue;
                }

                if (editors.ContainsKey(attr.PassType))
                {
                    continue;
                }

                editors[attr.PassType] = (PassEditor)Activator.CreateInstance(type);
            }
        }
    }
}
