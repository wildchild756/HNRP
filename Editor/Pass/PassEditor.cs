// <copyright file="PassEditor.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using HN.HNRP;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// 单个 Pass 的绘制上下文：提供该 pass 参数缓存条目在
    /// <see cref="SerializedObject"/> 中的 <see cref="SerializedProperty"/>，
    /// 面板绘制一律经 SerializedProperty 读写（保证嵌套 struct 正确回写、撤销与
    /// 持久化），模板实例仅作为「默认值基线」参与差异判断。
    /// </summary>
    public sealed class RenderGraphPassGUIContext
    {
        /// <summary>被编辑的渲染图资源。</summary>
        public RenderGraphAsset Asset { get; }

        /// <summary>模板代码生成的默认 pass（只读基线）。</summary>
        public Pass TemplatePass { get; }

        /// <summary>该 pass 参数缓存元素（<c>passParameterCache.Array.data[i]</c>）。</summary>
        public SerializedProperty PassProperty { get; }

        /// <summary>参数缓存对象（serialized 数据目标，Apply 后与 target 同步）。</summary>
        public Pass CachePass => PassProperty?.managedReferenceValue as Pass;

        /// <summary>是否已具备可编辑缓存（经同步后通常为 true）。</summary>
        public bool HasCache => PassProperty != null && CachePass != null;

        /// <summary>Header 开关：读缓存元素的 isEnabled。</summary>
        public bool IsEnabled => PassProperty != null
            ? PassProperty.FindPropertyRelative("isEnabled").boolValue
            : TemplatePass.IsEnabled;

        /// <summary>上下文所属 serializedObject（供 GetFieldProperty 使用）。</summary>
        private readonly SerializedObject serializedObject;

        /// <summary>
        /// 初始化新的绘制上下文。
        /// </summary>
        /// <param name="serializedObject">资源所属序列化对象。</param>
        /// <param name="passProperty">该 pass 参数缓存元素 property。</param>
        /// <param name="asset">被编辑的渲染图资源。</param>
        /// <param name="templatePass">模板代码生成的默认 pass。</param>
        public RenderGraphPassGUIContext(
            SerializedObject serializedObject,
            SerializedProperty passProperty,
            RenderGraphAsset asset,
            Pass templatePass)
        {
            this.serializedObject = serializedObject;
            PassProperty = passProperty;
            Asset = asset;
            TemplatePass = templatePass;
        }

        /// <summary>
        /// 设置 Header 开关（isEnabled），写入缓存元素 property（帧末统一 Apply）。
        /// </summary>
        /// <param name="enabled">目标启用状态。</param>
        public void SetEnabled(bool enabled)
        {
            if (PassProperty == null)
            {
                return;
            }

            PassProperty.FindPropertyRelative("isEnabled").boolValue = enabled;
        }

        /// <summary>
        /// 取某参数叶子的 SerializedProperty（沿叶子反射路径逐级 FindPropertyRelative）。
        /// 面板只经该 property 修改参数。
        /// </summary>
        /// <param name="leaf">字段叶子。</param>
        /// <returns>对应 property；路径失效时为 <c>null</c>。</returns>
        public SerializedProperty GetFieldProperty(PassParametersGUI.FieldLeaf leaf)
        {
            if (PassProperty == null)
            {
                return null;
            }

            SerializedProperty property = PassProperty;
            foreach (FieldInfo field in leaf.Path)
            {
                property = property.FindPropertyRelative(field.Name);
                if (property == null)
                {
                    return null;
                }
            }

            return property;
        }

        /// <summary>
        /// 判断某参数是否「已覆盖」（缓存值 ≠ 模板默认值），用于加粗 label。
        /// </summary>
        /// <param name="leaf">字段叶子。</param>
        /// <returns>已覆盖返回 <c>true</c>。</returns>
        public bool IsLeafOverridden(PassParametersGUI.FieldLeaf leaf)
        {
            if (!HasCache)
            {
                return false;
            }

            object templateValue = PassParametersGUI.GetLeafValue(TemplatePass, leaf);
            object cacheValue = PassParametersGUI.GetLeafValue(CachePass, leaf);
            return !Equals(templateValue, cacheValue);
        }

        /// <summary>请求打开该 pass 的文档。当前文档缺失，仅输出占位提示。</summary>
        public void ShowDocumentation()
        {
            Debug.Log(
                $"[HNRP] {TemplatePass.GetType().Name}('{TemplatePass.PassName}') " +
                $"文档暂缺：待补充。");
        }

        /// <summary>使 serializedObject 与目标对象同步（一般在写入后调用）。</summary>
        public void Synchronize()
        {
            serializedObject.Update();
        }
    }

    /// <summary>
    /// 在 <see cref="RenderGraphAsset"/> 检视面板中显示 / 编辑某个 <see cref="Pass"/>
    /// 参数的抽象 Editor 基类。RenderGraphAsset 面板按 pass 类型经
    /// <see cref="PassEditorRegistry"/> 取回编辑器并调用
    /// <see cref="DrawPassParameters"/>。
    /// </summary>
    /// <remarks>
    /// <see cref="Pass"/> 是纯 C# 类（非 <see cref="UnityEngine.Object"/>），因此不能
    /// 用 Unity 的 <c>[CustomEditor]</c>。默认实现按「Volume 组件面板」样式把参数
    /// 暴露为可编辑控件（未修改显示模板预设值，修改后 label 加粗）；子类可覆写
    /// <see cref="DrawPassParameters"/> 实现自定义布局。Editor 无状态、可缓存单例。
    /// </remarks>
    public abstract class PassEditor
    {
        /// <summary>
        /// 绘制当前 pass 的参数主体。基类实现按「组（嵌套可序列化 struct）→ 参数行」
        /// 布局，每行经 SerializedProperty 绘制可编辑控件。
        /// </summary>
        /// <param name="context">当前 pass 的绘制上下文。</param>
        public virtual void DrawPassParameters(RenderGraphPassGUIContext context)
        {
            PassParametersGUI.DrawParameters(context);
        }
    }

    /// <summary>
    /// 没有专用编辑器绑定的 pass 类型使用的默认编辑器。
    /// </summary>
    public sealed class DefaultPassEditor : PassEditor
    {
        /// <summary>共享单例实例。</summary>
        public static readonly DefaultPassEditor Instance = new DefaultPassEditor();
    }

    /// <summary>
    /// Pass 参数绘制工具：反射收集 pass 的「可编辑参数字段」（嵌套 struct 展开为
    /// 组），每行经 <see cref="RenderGraphPassGUIContext.GetFieldProperty"/> 的
    /// SerializedProperty 绘制可编辑控件。此工具保持 Pass 类自身简洁 —— Pass 不引入
    /// 任何编辑器函数。
    /// </summary>
    public static class PassParametersGUI
    {
        /// <summary>
        /// 一个可编辑参数叶子：从 pass 实例根部到叶字段的反射路径 + 显示名。
        /// </summary>
        public sealed class FieldLeaf
        {
            /// <summary>反射取值路径（首个为 pass 上字段，其后为嵌套 struct 字段）。</summary>
            public FieldInfo[] Path;

            /// <summary>叶子显示名（不含父组前缀）。</summary>
            public string Label;

            /// <summary>叶子字段类型。</summary>
            public Type ValueType => Path[Path.Length - 1].FieldType;
        }

        /// <summary>
        /// 一组参数：同属一个嵌套可序列化 struct 字段的叶子合集，带组标题。
        /// </summary>
        private sealed class FieldGroup
        {
            public string Label;
            public List<FieldLeaf> Leaves = new();
        }

        /// <summary>
        /// 无需暴露为可编辑参数的字段名（黑名单）。例如 RenderOutputPass 的 flip
        /// 由相机上下文每帧驱动，不应被用户覆盖。
        /// </summary>
        private static readonly HashSet<string> HiddenLeafNames = new()
        {
            "flip",
        };

        /// <summary>类型 → 参数条目（叶子或组）缓存（编辑器脚本重编译后自动失效）。</summary>
        private static readonly Dictionary<Type, IReadOnlyList<object>> itemsByType = new();

        private const BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>参数行 label 列宽（固定，不依赖全局 labelWidth）。</summary>
        private const float LabelColumnWidth = 150f;

        /// <summary>
        /// 绘制一个 pass 的全部可编辑参数（分组 + 参数行，SerializedProperty 驱动）。
        /// </summary>
        /// <param name="context">pass 绘制上下文。</param>
        public static void DrawParameters(RenderGraphPassGUIContext context)
        {
            if (!context.HasCache)
            {
                EditorGUILayout.HelpBox(
                    "该 Pass 尚无参数缓存，无法编辑。请点击 Header 开关或重新选择资源。",
                    MessageType.Warning);
                return;
            }

            IReadOnlyList<object> items = GetParameterItems(context.TemplatePass.GetType());
            bool any = false;

            foreach (object item in items)
            {
                switch (item)
                {
                    case FieldGroup group when group.Leaves.Count > 0:
                        DrawGroupHeader(group.Label);
                        EditorGUI.indentLevel++;
                        foreach (FieldLeaf leaf in group.Leaves)
                        {
                            DrawLeaf(context, leaf);
                            any = true;
                        }

                        EditorGUI.indentLevel--;
                        break;

                    case FieldLeaf leaf:
                        DrawLeaf(context, leaf);
                        any = true;
                        break;
                }
            }

            if (!any)
            {
                EditorGUILayout.HelpBox("该 Pass 无可编辑参数。", MessageType.Info);
            }
        }

        /// <summary>从 pass 实例按叶子路径读取值（仅用于默认值对比）。</summary>
        public static object GetLeafValue(object root, FieldLeaf leaf)
        {
            object current = root;
            foreach (FieldInfo field in leaf.Path)
            {
                if (current == null)
                {
                    return null;
                }

                current = field.GetValue(current);
            }

            return current;
        }

        /// <summary>收集 pass 类型全部参数条目（叶子或组，带缓存）。</summary>
        private static IReadOnlyList<object> GetParameterItems(Type passType)
        {
            if (itemsByType.TryGetValue(passType, out var cached))
            {
                return cached;
            }

            var items = new List<object>();
            foreach (FieldInfo field in passType.GetFields(FieldFlags))
            {
                if (!IsEditableField(field))
                {
                    continue;
                }

                if (HiddenLeafNames.Contains(field.Name))
                {
                    continue;
                }

                if (typeof(PassSlot).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                if (IsExpandableStruct(field.FieldType))
                {
                    // 可序列化嵌套 struct → 参数组。
                    var group = new FieldGroup
                    {
                        Label = FriendlyName(field.Name),
                        Leaves = new List<FieldLeaf>(),
                    };
                    CollectNestedLeaves(field.FieldType, new[] { field }, group.Leaves);
                    items.Add(group);
                }
                else
                {
                    items.Add(new FieldLeaf
                    {
                        Path = new[] { field },
                        Label = FriendlyName(field.Name),
                    });
                }
            }

            itemsByType[passType] = items;
            return items;
        }

        /// <summary>把嵌套 struct 的可编辑子字段收集为叶子（path 延续父字段）。</summary>
        private static void CollectNestedLeaves(
            Type structType, IReadOnlyList<FieldInfo> parentPath, List<FieldLeaf> result)
        {
            foreach (FieldInfo field in structType.GetFields(FieldFlags))
            {
                if (!IsEditableField(field))
                {
                    continue;
                }

                if (HiddenLeafNames.Contains(field.Name))
                {
                    continue;
                }

                var path = new FieldInfo[parentPath.Count + 1];
                for (int i = 0; i < parentPath.Count; i++)
                {
                    path[i] = parentPath[i];
                }

                path[parentPath.Count] = field;

                if (IsExpandableStruct(field.FieldType))
                {
                    CollectNestedLeaves(field.FieldType, path, result);
                }
                else
                {
                    result.Add(new FieldLeaf
                    {
                        Path = path,
                        Label = FriendlyName(field.Name),
                    });
                }
            }
        }

        private static bool IsEditableField(FieldInfo field)
        {
            if (field.IsInitOnly || field.IsLiteral)
            {
                return false;
            }

            // public 字段或带 [SerializeField] 的私有字段才进入参数面。
            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }

        private static bool IsExpandableStruct(Type type)
        {
            if (!type.IsValueType || type.IsEnum || type.IsPrimitive)
            {
                return false;
            }

            if (type == typeof(string) || type == typeof(decimal))
            {
                return false;
            }

            // 这些是 Unity 基础 struct，视为单个可绘制叶子而非容器。
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
                || type == typeof(Color) || type == typeof(Color32)
                || type == typeof(Bounds) || type == typeof(Rect) || type == typeof(Quaternion))
            {
                return false;
            }

            // 有可序列化字段的嵌套 struct 才作为组展开。
            foreach (FieldInfo field in type.GetFields(FieldFlags))
            {
                if (IsEditableField(field))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FriendlyName(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c)
                    && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>绘制参数组标题（小标题，粗体，区别于普通参数行）。</summary>
        private static void DrawGroupHeader(string label)
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private static void DrawLeaf(RenderGraphPassGUIContext context, FieldLeaf leaf)
        {
            SerializedProperty property = context.GetFieldProperty(leaf);
            if (property == null)
            {
                return;
            }

            bool overridden = context.IsLeafOverridden(leaf);
            GUIStyle labelStyle = overridden ? EditorStyles.boldLabel : EditorStyles.label;

            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var labelRect = new Rect(row.x, row.y, LabelColumnWidth, row.height);
            GUI.Label(labelRect, new GUIContent(leaf.Label), labelStyle);

            var fieldRect = new Rect(
                labelRect.xMax + 4f,
                row.y,
                Mathf.Max(40f, row.xMax - labelRect.xMax - 4f),
                row.height);

            // 关键：经 SerializedProperty 绘制控件 —— 修改会写入 serializedObject，
            // 由容器编辑器在帧末统一 ApplyModifiedProperties() 提交。
            EditorGUI.PropertyField(fieldRect, property, GUIContent.none);
        }
    }
}
