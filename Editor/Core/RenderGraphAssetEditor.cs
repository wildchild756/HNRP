// <copyright file="RenderGraphAssetEditor.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using HN.HNRP;
using CoreStyles = UnityEditor.Rendering.CoreEditorStyles;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// <see cref="RenderGraphAsset"/> 的自定义检视面板，参考 CoreRP Volume 面板布局。
    /// 资源存 <see cref="RenderGraphKind"/>、渲染图设置与按 pass 实例名的参数缓存
    /// （L1）。面板打开时把模板代码的 pass 列表同步为参数缓存（Volume Profile 模型：
    /// 每个 pass 恒有一个可编辑参数缓存，未修改的值等于模板默认）；所有修改经
    /// <see cref="SerializedObject"/> 提交，保证嵌套 struct 正确回写、Undo 与持久化。
    /// </summary>
    [CustomEditor(typeof(RenderGraphAsset))]
    public sealed class RenderGraphAssetEditor : UnityEditor.Editor
    {
        #region 字段

        private SerializedProperty settingsProp;
        private SerializedProperty shEvalModeProp;
        private SerializedProperty allowHDRProp;
        private SerializedProperty cacheProp;

        private bool settingsExpanded = true;

        // 由模板构建代码生成的预览数据（只读基线）。仅在资源 kind 变化时刷新。
        private RenderGraphKind previewKind;
        private List<Pass> previewPasses;
        private readonly List<bool> passExpanded = new();

        // Header 展开状态持久化（EditorPrefs，按资源）。跨会话保留，不写入资源。
        private const string ExpandedPrefsPrefix = "HNRP.RenderGraphAsset.Expanded.";
        private string expandedPrefsKey;
        private bool expandedStateLoaded;
        private bool expandedStateDirty;

        #endregion

        #region Unity 生命周期

        private void OnEnable()
        {
            InitProperties();

            // 切换目标资源后重新加载其展开状态。
            expandedPrefsKey = BuildExpandedPrefsKey();
            expandedStateLoaded = false;
            expandedStateDirty = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 同步参数缓存：补齐模板代码新增 pass、清理模板已删除 pass 的孤立缓存。
            EnsurePreview();
            SynchronizeParameterCaches();

            DrawScriptField();
            DrawKindSection();
            DrawSettingsSection();
            DrawPassesSection();

            // 展开状态变化时写入 EditorPrefs（仅脏时写，避免每帧落盘）。
            if (expandedStateDirty && expandedPrefsKey != null)
            {
                SaveExpandedState();
                expandedStateDirty = false;
            }

            // 参数缓存 / 设置在面板中被修改时自增修订号，使运行时重建 pass 列表
            // （运行时仅比较模板引用相等无法感知同一资源上的参数改动）。
            if (serializedObject.ApplyModifiedProperties())
            {
                ((RenderGraphAsset)target).BumpParameterRevision();
                EditorUtility.SetDirty(target);
            }
        }

        #endregion

        #region 属性初始化

        private void InitProperties()
        {
            settingsProp = serializedObject.FindProperty("settings");
            if (settingsProp != null)
            {
                shEvalModeProp = settingsProp.FindPropertyRelative(nameof(RenderGraphSettings.SHEvalMode));
                allowHDRProp = settingsProp.FindPropertyRelative(nameof(RenderGraphSettings.AllowHDR));
            }

            cacheProp = serializedObject.FindProperty("passParameterCache");
        }

        #endregion

        #region 顶部区

        private static void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                if (Selection.activeObject is ScriptableObject so)
                {
                    EditorGUILayout.ObjectField(
                        "Script",
                        MonoScript.FromScriptableObject(so),
                        typeof(MonoScript),
                        allowSceneObjects: false);
                }
            }
        }

        private void DrawKindSection()
        {
            var asset = (RenderGraphAsset)target;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Template", asset.Kind);
            }

            if (asset.Kind == RenderGraphKind.None)
            {
                EditorGUILayout.HelpBox(
                    "此资源未关联任何 RenderGraphTemplate。模板资源由 " +
                    "RenderGraphTemplates.EnsureAll() 自动创建/维护；如需重建请删除旧资源。",
                    MessageType.Warning);
            }
        }

        private void DrawSettingsSection()
        {
            settingsExpanded = EditorGUILayout.Foldout(
                settingsExpanded,
                "Render Graph Settings",
                toggleOnLabelClick: true);

            if (!settingsExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;

            if (shEvalModeProp != null)
            {
                EditorGUILayout.PropertyField(shEvalModeProp,
                    new GUIContent("SH Eval Mode",
                        "球谐光照求值模式：PerVertex、Mixed 或 PerPixel。"));
            }

            if (allowHDRProp != null)
            {
                EditorGUILayout.PropertyField(allowHDRProp,
                    new GUIContent("Allow HDR",
                        "启用后，渲染图可以分配 HDR 渲染目标。"));
            }

            EditorGUI.indentLevel--;
        }

        #endregion

        #region Pass 列表（仿 Volume）

        private void DrawPassesSection()
        {
            if (previewPasses == null || previewPasses.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "该模板没有 Pass。pass 结构与连接由模板构建代码定义。",
                    MessageType.Info);
                return;
            }

            EnsureExpandedCount(passExpanded, previewPasses.Count);

            // 首次绘制本资源时加载跨会话保存的展开状态。
            if (!expandedStateLoaded)
            {
                LoadExpandedState();
                expandedStateLoaded = true;
            }

            for (int i = 0; i < previewPasses.Count; i++)
            {
                if (i > 0)
                {
                    UnityEditor.Rendering.CoreEditorUtils.DrawSplitter();
                }

                Pass pass = previewPasses[i];
                if (pass == null)
                {
                    continue;
                }

                SerializedProperty passProperty = FindPassProperty(pass);
                if (passProperty == null)
                {
                    continue;
                }

                bool oldExpanded = passExpanded[i];
                bool expanded = DrawPassHeader(passProperty, pass, oldExpanded);
                passExpanded[i] = expanded;
                if (expanded != oldExpanded)
                {
                    expandedStateDirty = true;
                }

                if (!expanded)
                {
                    continue;
                }

                var context = new RenderGraphPassGUIContext(
                    serializedObject, passProperty, (RenderGraphAsset)target, pass);

                using (new EditorGUI.DisabledScope(!context.IsEnabled))
                using (new EditorGUI.IndentLevelScope(1))
                {
                    EditorGUILayout.Space(2f);
                    PassEditor editor = PassEditorRegistry.GetEditor(pass.GetType());
                    editor.DrawPassParameters(context);
                }

                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// 绘制单个 pass 的 Volume 风格 Header：背景条 + 折叠箭头 + 启用开关
        /// （绑定缓存元素 isEnabled）+ 标题 + 帮助按钮（?）+ 三点菜单。
        /// </summary>
        /// <param name="passProperty">该 pass 参数缓存元素。</param>
        /// <param name="pass">模板 pass（基线）。</param>
        /// <param name="expanded">当前展开状态。</param>
        /// <returns>折叠后状态。</returns>
        private bool DrawPassHeader(SerializedProperty passProperty, Pass pass, bool expanded)
        {
            GetHeaderRects(out Rect labelRect, out Rect foldoutRect, out Rect toggleRect,
                out Rect backgroundRect);

            // 背景条
            float tint = EditorGUIUtility.isProSkin ? 0.1f : 1f;
            EditorGUI.DrawRect(backgroundRect,
                new Color(tint, tint, tint, 0.2f));

            // 标题
            string title = pass.PassName;
            if (!string.IsNullOrEmpty(pass.PassName)
                && pass.GetType().Name != pass.PassName)
            {
                title = $"{pass.PassName} ({pass.GetType().Name})";
            }

            SerializedProperty enabledProp = passProperty.FindPropertyRelative("isEnabled");
            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
            }

            // 折叠箭头
            bool newExpanded = GUI.Toggle(foldoutRect, expanded, GUIContent.none,
                EditorStyles.foldout);

            // 启用开关：写入缓存元素 isEnabled（帧末 Apply）
            bool newEnabled = GUI.Toggle(toggleRect, enabledProp.boolValue,
                GUIContent.none, CoreStyles.smallTickbox);
            if (newEnabled != enabledProp.boolValue)
            {
                enabledProp.boolValue = newEnabled;
                Repaint();
            }

            // Header 右侧：三点菜单 + 帮助按钮
            var context = new RenderGraphPassGUIContext(
                serializedObject, passProperty, (RenderGraphAsset)target, pass);
            var menuRect = new Rect(labelRect.xMax + 3f + 16f + 5f, labelRect.y + 1f, 16f, 16f);
            if (GUI.Button(menuRect,
                    new GUIContent(CoreStyles.paneOptionsIcon, "Pass 菜单"),
                    CoreStyles.contextMenuStyle))
            {
                ShowPassContextMenu(new Vector2(menuRect.x, menuRect.yMax), pass);
            }

            var helpRect = new Rect(menuRect.x - 16f - 2f, menuRect.y, 16f, 16f);
            if (GUI.Button(helpRect,
                    new GUIContent(CoreStyles.iconHelp, "打开该 Pass 的参考文档"),
                    CoreStyles.iconHelpStyle))
            {
                context.ShowDocumentation();
            }

            // 点击背景条切换折叠
            var e = Event.current;
            if (e.type == EventType.MouseDown && backgroundRect.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    newExpanded = !newExpanded;
                }
                else if (e.button == 1)
                {
                    ShowPassContextMenu(e.mousePosition, pass);
                }

                e.Use();
            }

            return newExpanded;
        }

        private static void GetHeaderRects(
            out Rect labelRect, out Rect foldoutRect, out Rect toggleRect,
            out Rect backgroundRect)
        {
            backgroundRect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(1f, 17f));

            labelRect = backgroundRect;
            labelRect.xMin += 32f;
            labelRect.xMax -= 20f + 16f + 5f;

            foldoutRect = backgroundRect;
            foldoutRect.y += 1f;
            foldoutRect.width = 13f;
            foldoutRect.height = 13f;

            toggleRect = backgroundRect;
            toggleRect.x += 16f;
            toggleRect.y += 2f;
            toggleRect.width = 13f;
            toggleRect.height = 13f;

            // 背景条横向铺满
            backgroundRect.xMin = 0f;
            backgroundRect.width += 4f;
        }

        /// <summary>
        /// 三点菜单：把当前 pass 参数「回复模板默认」——用模板实例整体替换缓存条目。
        /// </summary>
        /// <param name="position">菜单锚点。</param>
        /// <param name="pass">目标模板 pass。</param>
        private void ShowPassContextMenu(Vector2 position, Pass pass)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Reset Parameters（回复模板默认）"),
                false,
                () => ResetParametersToTemplate(pass));
            menu.DropDown(new Rect(position, Vector2.zero));
        }

        #endregion

        #region 参数缓存同步

        /// <summary>
        /// 同步参数缓存与模板代码 pass 列表：
        /// 1) 补齐模板代码新增 pass 的缓存（值 = 模板默认，Volume Profile 模型）；
        /// 2) 删除模板已移除 pass 的孤立缓存条目。
        /// 变更经 serializedObject 立即提交（在绘制 pass 列表前执行，保证后续
        /// 所有元素 property 有效）。
        /// </summary>
        private void SynchronizeParameterCaches()
        {
            if (cacheProp == null || previewPasses == null || previewPasses.Count == 0)
            {
                return;
            }

            bool changed = false;

            // 删除孤立缓存。
            for (int i = cacheProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = cacheProp.GetArrayElementAtIndex(i);
                var cached = element.managedReferenceValue as Pass;
                if (cached == null || !ContainsInPreview(cached))
                {
                    cacheProp.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }

            // 补齐缺失缓存。
            foreach (Pass pass in previewPasses)
            {
                if (FindPassProperty(pass) != null)
                {
                    continue;
                }

                Pass cachePass;
                try
                {
                    cachePass = (Pass)Activator.CreateInstance(
                        pass.GetType(), pass.PassName);
                    PassParameterCopy.CopyParameters(pass, cachePass);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"RenderGraphAssetEditor: 无法为 '{pass.PassName}' 创建参数缓存。{ex.Message}");
                    continue;
                }

                int newIndex = cacheProp.arraySize;
                cacheProp.InsertArrayElementAtIndex(newIndex);
                cacheProp.GetArrayElementAtIndex(newIndex).managedReferenceValue = cachePass;
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                ((RenderGraphAsset)target).BumpParameterRevision();
                EditorUtility.SetDirty(target);
            }
        }

        /// <summary>按类型与实例名在缓存数组中查找该 pass 的元素 property。</summary>
        private SerializedProperty FindPassProperty(Pass templatePass)
        {
            if (cacheProp == null)
            {
                return null;
            }

            for (int i = 0; i < cacheProp.arraySize; i++)
            {
                SerializedProperty element = cacheProp.GetArrayElementAtIndex(i);
                if (element.managedReferenceValue is Pass cached
                    && cached.GetType() == templatePass.GetType()
                    && cached.PassName == templatePass.PassName)
                {
                    return element;
                }
            }

            return null;
        }

        /// <summary>预览列表中是否含同类型同名的模板 pass。</summary>
        private bool ContainsInPreview(Pass cached)
        {
            foreach (Pass pass in previewPasses)
            {
                if (pass != null
                    && pass.GetType() == cached.GetType()
                    && pass.PassName == cached.PassName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 回复模板默认：用模板实例替换该 pass 的缓存条目（删除旧条目，同位置插入
        /// 快照），并立即提交。
        /// </summary>
        /// <param name="pass">目标模板 pass。</param>
        private void ResetParametersToTemplate(Pass pass)
        {
            if (cacheProp == null)
            {
                return;
            }

            Undo.RecordObject(target, "Reset Pass Parameters");

            int targetIndex = -1;
            for (int i = 0; i < cacheProp.arraySize; i++)
            {
                SerializedProperty element = cacheProp.GetArrayElementAtIndex(i);
                if (element.managedReferenceValue is Pass cached
                    && cached.GetType() == pass.GetType()
                    && cached.PassName == pass.PassName)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                return;
            }

            cacheProp.DeleteArrayElementAtIndex(targetIndex);

            Pass cachePass;
            try
            {
                cachePass = (Pass)Activator.CreateInstance(pass.GetType(), pass.PassName);
                PassParameterCopy.CopyParameters(pass, cachePass);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"RenderGraphAssetEditor: 重置 '{pass.PassName}' 参数失败。{ex.Message}");
                return;
            }

            cacheProp.InsertArrayElementAtIndex(targetIndex);
            cacheProp.GetArrayElementAtIndex(targetIndex).managedReferenceValue = cachePass;

            serializedObject.ApplyModifiedProperties();
            ((RenderGraphAsset)target).BumpParameterRevision();
            EditorUtility.SetDirty(target);
        }

        #endregion

        #region 模板预览

        /// <summary>
        /// 每当资源 kind 变化时执行一次模板构建代码，刷新只读预览（pass 基线）。
        /// </summary>
        private void EnsurePreview()
        {
            var asset = (RenderGraphAsset)target;
            if (asset.Kind == previewKind && previewPasses != null)
            {
                return;
            }

            previewKind = asset.Kind;
            previewPasses = new List<Pass>();

            RenderGraphTemplate template = RenderGraphTemplates.Get(asset.Kind);
            if (template == null)
            {
                return;
            }

            previewPasses = template.CreateBlueprint().Passes;
        }

        #endregion

        #region 辅助

        private static void EnsureExpandedCount(List<bool> expanded, int count)
        {
            while (expanded.Count < count)
            {
                expanded.Add(false);
            }
        }

        #endregion

        #region 展开状态持久化（EditorPrefs）

        /// <summary>构造按资源定位的 EditorPrefs 键（优先 GUID，无 GUID 用资源名）。</summary>
        private static string BuildExpandedPrefsKey(RenderGraphAsset asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(path);
            string identity = string.IsNullOrEmpty(guid) ? asset.name : guid;
            return ExpandedPrefsPrefix + identity;
        }

        private string BuildExpandedPrefsKey()
        {
            return target != null ? BuildExpandedPrefsKey((RenderGraphAsset)target) : null;
        }

        /// <summary>把当前各 pass 展开状态保存为逗号分隔的 pass 名列表。</summary>
        private void SaveExpandedState()
        {
            if (previewPasses == null || previewPasses.Count == 0)
            {
                return;
            }

            var expandedNames = new List<string>();
            for (int i = 0; i < previewPasses.Count && i < passExpanded.Count; i++)
            {
                if (passExpanded[i] && previewPasses[i] != null)
                {
                    expandedNames.Add(previewPasses[i].PassName);
                }
            }

            EditorPrefs.SetString(expandedPrefsKey, string.Join(",", expandedNames));
        }

        /// <summary>从 EditorPrefs 加载各 pass 展开状态（默认全部收起）。</summary>
        private void LoadExpandedState()
        {
            if (previewPasses == null || expandedPrefsKey == null)
            {
                return;
            }

            string saved = EditorPrefs.GetString(expandedPrefsKey, string.Empty);
            var expandedNames = new HashSet<string>(
                saved.Split(','),
                System.StringComparer.Ordinal);

            for (int i = 0; i < previewPasses.Count && i < passExpanded.Count; i++)
            {
                passExpanded[i] = previewPasses[i] != null
                    && expandedNames.Contains(previewPasses[i].PassName);
            }
        }

        #endregion
    }
}
