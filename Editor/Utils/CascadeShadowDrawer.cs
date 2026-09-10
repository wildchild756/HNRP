using HN.HNRP;
using UnityEditor;
using UnityEngine;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// <see cref="CascadeShadowAttribute"/> 的自定义绘制器。
    /// 平行光绘制级数、分辨率、级联滑条与选中级联参数；
    /// 点光/聚光只支持单级阴影，不绘制级数下拉与级联滑条。
    /// </summary>
    /// <remarks>
    /// 这里完全基于 <see cref="PropertyDrawer"/> 传入的 <c>position</c> 做绝对布局，
    /// 不使用 GUILayout / BeginArea —— 因为该 drawer 位于 CoreEditorDrawer 的 foldout
    /// 布局组内，BeginArea 的坐标空间不匹配会导致内容被画到面板左上角。
    /// </remarks>
    [CustomPropertyDrawer(typeof(CascadeShadowAttribute))]
    public class CascadeShadowDrawer : PropertyDrawer
    {
        private const int MaxCascadeCount = CascadeShadowSettings.MaxCascadeCount;

        private const float SliderHandleRowHeight = 13f;
        private const float SliderBarHeight = 28f;
        private const float SliderBarMargin = 2f;
        private const float SliderBottomMargin = 15f;
        private const float SliderTotalHeight = SliderHandleRowHeight + SliderBarHeight + SliderBarMargin * 2f + SliderBottomMargin;

        private const float HandleWidth = 12f;
        private const float HandleHeight = 16f;
        private const float PartitionWidth = 2f;
        private const float InspectorLabelWidth = 96f;

        private static readonly string[] CascadeCountNames = { "1", "2", "4", "8" };
        private static readonly int[] CascadeCountValues = { 1, 2, 4, 8 };

        private static readonly ResolutionType[] ResolutionValues =
        {
            ResolutionType.Low, ResolutionType.Medium, ResolutionType.High, ResolutionType.Ultra
        };

        private static readonly string[] UpdateModeNames = { "Every Frame", "On Demand", "Custom" };
        private static readonly int[] UpdateModeValues = { 0, 1, 2 };

        // 8 级级联配色，扩展自 CoreRP 的 ShadowCascadeGUI。
        private static readonly Color[] CascadeColors =
        {
            new Color(0.50f, 0.50f, 0.70f, 1.0f),
            new Color(0.50f, 0.70f, 0.50f, 1.0f),
            new Color(0.70f, 0.70f, 0.50f, 1.0f),
            new Color(0.70f, 0.50f, 0.50f, 1.0f),
            new Color(0.50f, 0.65f, 0.75f, 1.0f),
            new Color(0.65f, 0.50f, 0.75f, 1.0f),
            new Color(0.60f, 0.60f, 0.60f, 1.0f),
            new Color(0.75f, 0.60f, 0.50f, 1.0f)
        };

        private static readonly Color SelectionColor = new Color(1.0f, 0.85f, 0.1f, 1.0f);

        private static readonly GUIStyle SliderBackgroundStyle = new GUIStyle("LODSliderRange");
        private static readonly GUIStyle CenteredLabelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };

        private static readonly int SliderControlHash = "HNRP.CascadeShadowSlider".GetHashCode();

        // 拖拽状态在重绘之间保持。
        private static int activeHandle = -1;

        private static GUIStyle downSnatchStyle;

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!HasSerializedChildren(property))
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            float line = EditorGUIUtility.singleLineHeight;
            float space = EditorGUIUtility.standardVerticalSpacing;
            bool usesCascade = UsesCascade(property);

            float height = line;
            height += space + line;
            if (usesCascade)
            {
                height += space + SliderTotalHeight;

                if (GetSelectedCascade(property) > 0)
                {
                    height += space + line; // Near Boundary
                }
                height += space + line;     // Far Boundary
            }

            SerializedProperty updateModeProperty = property.FindPropertyRelative("shadowUpdateMode");
            if (updateModeProperty != null
                && updateModeProperty.intValue == (int)ShadowUpdateModeType.Custom)
            {
                height += space + line;     // Update Interval
            }

            return height + 2f;
        }

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty countProperty = property.FindPropertyRelative("cascadeCount");
            SerializedProperty resolutionProperty = property.FindPropertyRelative("cascadeResolution");
            SerializedProperty splitsProperty = property.FindPropertyRelative("cascadeSplits");
            SerializedProperty updateModeProperty = property.FindPropertyRelative("shadowUpdateMode");
            SerializedProperty timeSlicesProperty = property.FindPropertyRelative("cascadeTimeSlices");

            if (!HasSerializedChildren(property)
                || splitsProperty.arraySize < MaxCascadeCount
                || timeSlicesProperty.arraySize < MaxCascadeCount)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            LightType lightType = GetLightType(property);
            bool usesCascade = lightType == LightType.Directional;

            // 点光/聚光只支持单级阴影，强制级数为 1。
            if (!usesCascade && countProperty.intValue != 1)
            {
                countProperty.intValue = 1;
            }
            int cascadeCount = usesCascade ? NormalizeCascadeCount(countProperty.intValue) : 1;

            float line = EditorGUIUtility.singleLineHeight;
            float space = EditorGUIUtility.standardVerticalSpacing;
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = InspectorLabelWidth;

            float y = position.y;

            DrawCountAndResolution(
                new Rect(position.x, y, position.width, line),
                countProperty, resolutionProperty, usesCascade, lightType);
            y += line + space;

            DrawUpdateMode(new Rect(position.x, y, position.width, line), updateModeProperty);
            y += line + space;

            int selectedCascade = 0;
            if (usesCascade)
            {
                selectedCascade = DrawCascadeSlider(
                    new Rect(position.x, y, position.width, SliderTotalHeight),
                    splitsProperty,
                    cascadeCount,
                    property.propertyPath);
                y += SliderTotalHeight + space;
            }

            // 级联信息按需显示：不需要时直接隐藏，而不是灰显。
            if (usesCascade && selectedCascade > 0)
            {
                DrawNearBoundary(
                    new Rect(position.x, y, position.width, line),
                    splitsProperty, cascadeCount, selectedCascade);
                y += line + space;
            }

            if (usesCascade)
            {
                DrawFarBoundary(
                    new Rect(position.x, y, position.width, line),
                    splitsProperty, cascadeCount, selectedCascade);
                y += line + space;
            }

            if (updateModeProperty.intValue == (int)ShadowUpdateModeType.Custom)
            {
                DrawUpdateInterval(
                    new Rect(position.x, y, position.width, line),
                    timeSlicesProperty, selectedCascade);
                y += line + space;
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private static bool HasSerializedChildren(SerializedProperty property)
        {
            return property.FindPropertyRelative("cascadeCount") != null
                && property.FindPropertyRelative("cascadeResolution") != null
                && property.FindPropertyRelative("cascadeSplits") != null
                && property.FindPropertyRelative("shadowUpdateMode") != null
                && property.FindPropertyRelative("cascadeTimeSlices") != null;
        }

        private static LightType GetLightType(SerializedProperty property)
        {
            if (property.serializedObject.targetObject is HNAdditionalLightData additionalLightData)
            {
                Light light = additionalLightData.BuiltinLight;
                if (light != null)
                {
                    return light.type;
                }
            }
            return LightType.Directional;
        }

        private static bool UsesCascade(SerializedProperty property)
        {
            return GetLightType(property) == LightType.Directional;
        }

        private static void DrawCountAndResolution(
            Rect rowRect,
            SerializedProperty countProperty,
            SerializedProperty resolutionProperty,
            bool usesCascade,
            LightType lightType)
        {
            if (usesCascade)
            {
                float half = (rowRect.width - 6f) * 0.5f;
                Rect countRect = new Rect(rowRect.x, rowRect.y, half, rowRect.height);
                Rect resolutionRect = new Rect(rowRect.x + half + 6f, rowRect.y, half, rowRect.height);

                EditorGUI.BeginChangeCheck();
                int newCount = EditorGUI.IntPopup(
                    EditorGUI.PrefixLabel(countRect, Styles.CascadeCount),
                    NormalizeCascadeCount(countProperty.intValue),
                    CascadeCountNames,
                    CascadeCountValues);
                if (EditorGUI.EndChangeCheck())
                {
                    countProperty.intValue = newCount;
                    resolutionProperty.intValue = (int)CascadeShadowUtils.ClampResolution(
                        (CascadeCountType)newCount,
                        (ResolutionType)resolutionProperty.intValue);
                }

                DrawResolutionDropdown(resolutionRect, countProperty, resolutionProperty, Styles.Resolution);
                return;
            }

            GUIContent resolutionLabel = lightType == LightType.Point ? Styles.ResolutionPerFace : Styles.Resolution;
            DrawResolutionDropdown(rowRect, countProperty, resolutionProperty, resolutionLabel);
        }

        private static void DrawResolutionDropdown(
            Rect rect,
            SerializedProperty countProperty,
            SerializedProperty resolutionProperty,
            GUIContent label)
        {
            Rect fieldRect = EditorGUI.PrefixLabel(rect, label);
            if (EditorGUI.DropdownButton(
                    fieldRect,
                    new GUIContent(resolutionProperty.intValue.ToString()),
                    FocusType.Keyboard))
            {
                ShowResolutionMenu(fieldRect, countProperty, resolutionProperty);
            }
        }

        private static void ShowResolutionMenu(Rect position, SerializedProperty countProperty, SerializedProperty resolutionProperty)
        {
            GenericMenu menu = new GenericMenu();
            CascadeCountType count = (CascadeCountType)NormalizeCascadeCount(countProperty.intValue);

            for (int i = 0; i < ResolutionValues.Length; i++)
            {
                ResolutionType resolution = ResolutionValues[i];
                GUIContent content = new GUIContent(((int)resolution).ToString());
                bool selected = resolutionProperty.intValue == (int)resolution;

                if (CascadeShadowUtils.IsResolutionSupported(count, resolution))
                {
                    ResolutionType captured = resolution;
                    menu.AddItem(content, selected, () =>
                    {
                        resolutionProperty.intValue = (int)captured;
                        resolutionProperty.serializedObject.ApplyModifiedProperties();
                    });
                }
                else
                {
                    menu.AddDisabledItem(content, selected);
                }
            }

            menu.DropDown(position);
        }

        private static void DrawUpdateMode(Rect rowRect, SerializedProperty updateModeProperty)
        {
            EditorGUI.BeginChangeCheck();
            int mode = EditorGUI.IntPopup(
                EditorGUI.PrefixLabel(rowRect, Styles.UpdateMode),
                updateModeProperty.intValue,
                UpdateModeNames,
                UpdateModeValues);
            if (EditorGUI.EndChangeCheck())
            {
                updateModeProperty.intValue = mode;
            }
        }

        private static int DrawCascadeSlider(Rect rowRect, SerializedProperty splitsProperty, int cascadeCount, string propertyPath)
        {
            string selectionKey = "HNRP.CascadeShadow.SelectedCascade." + propertyPath;
            int selected = Mathf.Clamp(SessionState.GetInt(selectionKey, 0), 0, cascadeCount - 1);

            Rect barRect = new Rect(
                rowRect.x + SliderBarMargin,
                rowRect.y + SliderHandleRowHeight,
                rowRect.width - SliderBarMargin * 2f,
                SliderBarHeight);

            float maxDistance = GetSplitValue(splitsProperty, cascadeCount - 1);
            if (maxDistance <= 0f)
            {
                maxDistance = 1f;
            }
            float minGap = Mathf.Max(0.01f, maxDistance * 0.001f);

            float[] splitValues = new float[cascadeCount];
            for (int i = 0; i < cascadeCount; i++)
            {
                splitValues[i] = GetSplitValue(splitsProperty, i);
            }

            Rect[] blockRects = BuildBlockRects(barRect, splitValues, cascadeCount, maxDistance);

            HandleSliderInput(rowRect, barRect, blockRects, splitValues, splitsProperty, cascadeCount, maxDistance, minGap,
                selectionKey, ref selected);

            if (Event.current.type != EventType.Repaint)
            {
                return selected;
            }

            for (int i = 0; i < cascadeCount; i++)
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = CascadeColors[i % CascadeColors.Length];
                GUI.Box(blockRects[i], GUIContent.none, SliderBackgroundStyle);
                GUI.backgroundColor = previousBackground;

                Color previousColor = GUI.color;
                GUI.color = Color.black;
                GUI.Label(blockRects[i], $"{i}\n{splitValues[i]:F1}m", CenteredLabelStyle);
                GUI.color = previousColor;
            }

            for (int i = 0; i < cascadeCount - 1; i++)
            {
                EditorGUI.DrawRect(
                    new Rect(blockRects[i].xMax - PartitionWidth * 0.5f, barRect.y, PartitionWidth, barRect.height),
                    Color.black);
            }

            DrawSelectionOutline(blockRects[selected]);

            for (int i = 0; i < cascadeCount - 1; i++)
            {
                Rect handleRect = GetHandleRect(blockRects[i].xMax, barRect);
                EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                DrawSnatchHandle(handleRect, CascadeColors[(i + 1) % CascadeColors.Length], activeHandle == i);
            }

            return selected;
        }

        private static void HandleSliderInput(
            Rect rowRect,
            Rect barRect,
            Rect[] blockRects,
            float[] splitValues,
            SerializedProperty splitsProperty,
            int cascadeCount,
            float maxDistance,
            float minGap,
            string selectionKey,
            ref int selected)
        {
            Event currentEvent = Event.current;
            int controlId = GUIUtility.GetControlID(SliderControlHash, FocusType.Passive, barRect);

            int hoveredHandle = -1;
            for (int i = 0; i < cascadeCount - 1; i++)
            {
                if (GetHandleRect(blockRects[i].xMax, barRect).Contains(currentEvent.mousePosition))
                {
                    hoveredHandle = i;
                }
            }

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (currentEvent.button != 0 || !rowRect.Contains(currentEvent.mousePosition))
                    {
                        break;
                    }

                    if (hoveredHandle >= 0)
                    {
                        GUIUtility.hotControl = controlId;
                        activeHandle = hoveredHandle;
                        selected = hoveredHandle;
                        SessionState.SetInt(selectionKey, selected);
                        currentEvent.Use();
                    }
                    else
                    {
                        int hoveredBlock = HitTestBlock(blockRects, currentEvent.mousePosition);
                        if (hoveredBlock >= 0)
                        {
                            selected = hoveredBlock;
                            SessionState.SetInt(selectionKey, selected);
                            GUI.changed = true;
                            currentEvent.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId || activeHandle < 0 || activeHandle >= cascadeCount - 1)
                    {
                        break;
                    }

                    float normalized = (currentEvent.mousePosition.x - barRect.x) / Mathf.Max(1f, barRect.width);
                    float draggedDistance = NormalizedToDistance(normalized, maxDistance);

                    float lower = activeHandle > 0 ? splitValues[activeHandle - 1] : 0f;
                    float upper = splitValues[activeHandle + 1];
                    float value = Mathf.Clamp(draggedDistance, lower + minGap, upper - minGap);

                    splitValues[activeHandle] = value;
                    splitsProperty.GetArrayElementAtIndex(activeHandle).floatValue = value;
                    GUI.changed = true;
                    currentEvent.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        activeHandle = -1;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        private static int GetSelectedCascade(SerializedProperty property)
        {
            string selectionKey = "HNRP.CascadeShadow.SelectedCascade." + property.propertyPath;
            int cascadeCount = UsesCascade(property)
                ? NormalizeCascadeCount(property.FindPropertyRelative("cascadeCount").intValue)
                : 1;
            return Mathf.Clamp(SessionState.GetInt(selectionKey, 0), 0, cascadeCount - 1);
        }

        private static void DrawNearBoundary(Rect rect, SerializedProperty splitsProperty, int cascadeCount, int selected)
        {
            float maxDistance = GetSplitValue(splitsProperty, cascadeCount - 1);
            float minGap = Mathf.Max(0.01f, maxDistance * 0.001f);

            float nearBoundary = GetSplitValue(splitsProperty, selected - 1);
            float farBoundary = GetSplitValue(splitsProperty, selected);

            EditorGUI.BeginChangeCheck();
            float newNear = EditorGUI.FloatField(rect, Styles.NearBoundary, nearBoundary);
            if (EditorGUI.EndChangeCheck())
            {
                float lower = selected >= 2 ? GetSplitValue(splitsProperty, selected - 2) : 0f;
                newNear = Mathf.Clamp(newNear, lower + minGap, farBoundary - minGap);
                splitsProperty.GetArrayElementAtIndex(selected - 1).floatValue = newNear;
            }
        }

        private static void DrawFarBoundary(Rect rect, SerializedProperty splitsProperty, int cascadeCount, int selected)
        {
            float maxDistance = GetSplitValue(splitsProperty, cascadeCount - 1);
            float minGap = Mathf.Max(0.01f, maxDistance * 0.001f);

            float nearBoundary = selected == 0 ? 0f : GetSplitValue(splitsProperty, selected - 1);
            float farBoundary = GetSplitValue(splitsProperty, selected);

            EditorGUI.BeginChangeCheck();
            float newFar = EditorGUI.FloatField(rect, Styles.FarBoundary, farBoundary);
            if (EditorGUI.EndChangeCheck())
            {
                float upper = selected < cascadeCount - 1 ? GetSplitValue(splitsProperty, selected + 1) : float.MaxValue;
                newFar = Mathf.Clamp(newFar, nearBoundary + minGap, upper - minGap);
                splitsProperty.GetArrayElementAtIndex(selected).floatValue = newFar;
            }
        }

        private static void DrawUpdateInterval(Rect rect, SerializedProperty timeSlicesProperty, int selected)
        {
            int interval = timeSlicesProperty.GetArrayElementAtIndex(selected).intValue;

            EditorGUI.BeginChangeCheck();
            int newInterval = EditorGUI.IntField(rect, Styles.UpdateInterval, interval);
            if (EditorGUI.EndChangeCheck())
            {
                timeSlicesProperty.GetArrayElementAtIndex(selected).intValue = Mathf.Max(0, newInterval);
            }
        }

        private static Rect[] BuildBlockRects(Rect barRect, float[] splitValues, int cascadeCount, float maxDistance)
        {
            Rect[] blockRects = new Rect[cascadeCount];
            float x = barRect.x;

            for (int i = 0; i < cascadeCount; i++)
            {
                float right = i == cascadeCount - 1
                    ? barRect.xMax
                    : barRect.x + DistanceToNormalized(splitValues[i], maxDistance) * barRect.width;

                blockRects[i] = new Rect(x, barRect.y, right - x, barRect.height);
                x = right;
            }

            return blockRects;
        }

        /// <summary>
        /// 把世界空间距离映射到 [0,1] 的滑条位置。
        /// 采用对数映射，使近处级联占据更大宽度，避免近处几级在滑条上重叠。
        /// </summary>
        private static float DistanceToNormalized(float distance, float maxDistance)
        {
            float denominator = Mathf.Log(1f + maxDistance);
            if (denominator <= 0f)
            {
                return 0f;
            }

            return Mathf.Log(1f + Mathf.Clamp(distance, 0f, maxDistance)) / denominator;
        }

        /// <summary>
        /// <see cref="DistanceToNormalized"/> 的逆运算，把滑条位置还原成世界空间距离。
        /// </summary>
        private static float NormalizedToDistance(float normalized, float maxDistance)
        {
            return Mathf.Exp(Mathf.Clamp01(normalized) * Mathf.Log(1f + maxDistance)) - 1f;
        }

        private static int HitTestBlock(Rect[] blockRects, Vector2 mousePosition)
        {
            for (int i = 0; i < blockRects.Length; i++)
            {
                if (blockRects[i].Contains(mousePosition))
                {
                    return i;
                }
            }
            return -1;
        }

        private static Rect GetHandleRect(float boundaryX, Rect barRect)
        {
            return new Rect(
                boundaryX - HandleWidth * 0.5f,
                barRect.y - SliderHandleRowHeight + 1f,
                HandleWidth,
                HandleHeight);
        }

        private static void DrawSelectionOutline(Rect rect)
        {
            const float thickness = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), SelectionColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), SelectionColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), SelectionColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), SelectionColor);
        }

        private static void DrawSnatchHandle(Rect rect, Color color, bool active)
        {
            GUIStyle style = GetDownSnatchStyle();
            Color previousColor = GUI.color;
            GUI.color = color * (active ? 1.4f : 1.0f);
            style.Draw(rect, false, false, false, false);
            GUI.color = previousColor;
        }

        private static GUIStyle GetDownSnatchStyle()
        {
            if (downSnatchStyle == null)
            {
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/DownSnatch.png");
                Texture2D focused = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.unity.render-pipelines.core/Editor/Lighting/Icons/DownSnatchFocused.png");

                downSnatchStyle = new GUIStyle
                {
                    normal = { background = normal },
                    hover = { background = normal },
                    focused = { background = focused }
                };
            }

            return downSnatchStyle;
        }

        private static float GetSplitValue(SerializedProperty splitsProperty, int index)
        {
            index = Mathf.Clamp(index, 0, splitsProperty.arraySize - 1);
            return splitsProperty.GetArrayElementAtIndex(index).floatValue;
        }

        private static int NormalizeCascadeCount(int rawValue)
        {
            return rawValue == 1 || rawValue == 2 || rawValue == 4 || rawValue == 8 ? rawValue : 4;
        }

        private static class Styles
        {
            public static readonly GUIContent CascadeCount = new GUIContent("Cascade Count");
            public static readonly GUIContent Resolution = new GUIContent("Resolution");
            public static readonly GUIContent ResolutionPerFace = new GUIContent("Resolution (per face)");
            public static readonly GUIContent UpdateMode = new GUIContent("Update Mode");
            public static readonly GUIContent NearBoundary = new GUIContent("Near Boundary");
            public static readonly GUIContent FarBoundary = new GUIContent("Far Boundary");
            public static readonly GUIContent UpdateInterval = new GUIContent("Update Interval");
        }
    }
}
