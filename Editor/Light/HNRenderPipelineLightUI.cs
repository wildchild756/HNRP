using System;
using System.Reflection;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using System.Linq;

namespace HN.HNRP.Editor
{
    using CED = CoreEditorDrawer<HNRenderPipelineSerializedLight>;

    public class HNRenderPipelineLightUI
    {
        public static CED.IDrawer[] Inspector()
        {
            return new CED.IDrawer[]
            {
                GeneralSettings(),
                SpotShapeSettings(),
                AreaShapeSettings(),
                EmissionSheetings(),
                RenderingContent(),
                ShadowsContent(),
            };
        }

#region General
        public static CED.IDrawer GeneralSettings()
        {
            return CED.FoldoutGroup(
                LightUI.Styles.generalHeader,
                Expandable.General,
                expandedState,
                DrawGeneralContent
            );
        }


        private static void DrawGeneralContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            int selectedLightType = p.settings.lightType.intValue;

            if(!Styles.lightTypeValues.Contains(p.settings.lightType.intValue))
            {
                if(p.settings.lightType.intValue == (int)LightType.Disc)
                {
                    selectedLightType = (int)LightType.Area;
                }
            }

            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, Styles.type, p.settings.lightType);
            EditorGUI.BeginChangeCheck();
            int type = EditorGUI.IntPopup(rect, Styles.type, selectedLightType, Styles.lightTypeTitles, Styles.lightTypeValues);
            if (EditorGUI.EndChangeCheck())
            {
                setGizmosDirty();
                p.settings.lightType.intValue = type;
            }
            EditorGUI.EndProperty();

            Light light = p.settings.light;
            var lightType = light.type;
            if (LightType.Directional != lightType && light == RenderSettings.sun)
            {
                EditorGUILayout.HelpBox(Styles.sunSourceWarning.text, MessageType.Warning);
            }

            if (!p.settings.lightType.hasMultipleDifferentValues)
            {
                using (new EditorGUI.DisabledScope(p.settings.isAreaLightType))
                    p.settings.DrawLightmapping();

                if (p.settings.isAreaLightType && p.settings.lightmapping.intValue != (int)LightmapBakeType.Baked)
                {
                    p.settings.lightmapping.intValue = (int)LightmapBakeType.Baked;
                    p.Apply();
                }
            }
        }

        static Func<int> setGizmosDirty = SetGizmosDirty();
        static Func<int> SetGizmosDirty()
        {
            var type = Type.GetType("UnityEditor.AnnotationUtility,UnityEditor");
            var method = type.GetMethod("SetGizmosDirty", BindingFlags.Static | BindingFlags.NonPublic);
            var lambda = Expression.Lambda<Func<int>>(Expression.Call(method));
            return lambda.Compile();
        }
#endregion

#region Shape
        public static CED.IDrawer SpotShapeSettings()
        {
            return CED.Conditional(
                (serializedLight, editor) => !serializedLight.settings.lightType.hasMultipleDifferentValues && serializedLight.settings.light.type == LightType.Spot,
                CED.FoldoutGroup(
                    LightUI.Styles.shapeHeader, 
                    Expandable.Shape, 
                    expandedState, 
                    DrawSpotShapeContent)
            );
        }

        public static CED.IDrawer AreaShapeSettings()
        {
            return CED.Conditional(
                (serializedLight, editor) =>
                {
                    if(serializedLight.settings.lightType.hasMultipleDifferentValues)
                        return false;
                    var lightType = serializedLight.settings.light.type;
                    return lightType == LightType.Rectangle || lightType == LightType.Disc;
                },
                CED.FoldoutGroup(
                    LightUI.Styles.shapeHeader, 
                    Expandable.Shape, 
                    expandedState, 
                    DrawAreaShapeContent)
            );
        }


        private static void DrawSpotShapeContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            p.settings.DrawInnerAndOuterSpotAngle();
        }

        private static void DrawAreaShapeContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            int selectedShape = p.settings.isAreaLightType ? p.settings.lightType.intValue : 0;

            // 处理所有不在默认集合中的光源
            if (!Styles.lightTypeValues.Contains(p.settings.lightType.intValue))
            {
                if (p.settings.lightType.intValue == (int)LightType.Disc)
                {
                    selectedShape = (int)LightType.Disc;
                }
            }

            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, Styles.areaLightShapeContent, p.settings.lightType);
            EditorGUI.BeginChangeCheck();
            int shape = EditorGUI.IntPopup(rect, Styles.areaLightShapeContent, selectedShape, Styles.areaLightShapeTitles, Styles.areaLightShapeValues);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(p.settings.light, "Adjust Light Shape");
                p.settings.lightType.intValue = shape;
            }
            EditorGUI.EndProperty();

            using (new EditorGUI.IndentLevelScope())
                p.settings.DrawArea();
        }

#endregion

#region Emission
        public static CED.IDrawer EmissionSheetings()
        {
            return CED.FoldoutGroup(
                LightUI.Styles.emissionHeader,
                Expandable.Emission,
                expandedState,
                CED.Group(
                    LightUI.DrawColor,
                    DrawEmissionContent
                )
            );
        }


        private static void DrawEmissionContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            p.settings.DrawIntensity();
            p.settings.DrawBounceIntensity();

            if (!p.settings.lightType.hasMultipleDifferentValues)
            {
                var lightType = p.settings.light.type;
                if (lightType != LightType.Directional)
                {
                    p.settings.DrawRange();
                }
            }

            DrawLightCookieContent(p, owner);
        }

        private static void DrawLightCookieContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            var settings = p.settings;
            if (settings.lightType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit light cookies from different light types.", MessageType.Info);
                return;
            }

            settings.DrawCookie();

            // Draw 2D cookie size for directional lights
            bool isDirectionalLight = settings.light.type == LightType.Directional;
            if (isDirectionalLight)
            {
                if (settings.cookie != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(p.lightCookieSizeProperty, Styles.LightCookieSize);
                    EditorGUILayout.PropertyField(p.lightCookieOffsetProperty, Styles.LightCookieOffset);
                    if (EditorGUI.EndChangeCheck())
                        UnityEditor.Experimental.Lightmapping.SetLightDirty((UnityEngine.Light)p.serializedObject.targetObject);
                }
            }
        }
#endregion

#region Rendering
        public static CED.IDrawer RenderingContent()
        {
            return CED.FoldoutGroup(
                LightUI.Styles.renderingHeader,
                Expandable.General,
                expandedState,
                DrawRenderingContent
            );
        }


        private static void DrawRenderingContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            EditorGUILayout.PropertyField(p.renderingLayerMask, Styles.renderingLayers);
            EditorGUILayout.PropertyField(p.settings.cullingMask, Styles.cullingMask);
            if (p.settings.cullingMask.intValue != -1)
            {
                EditorGUILayout.HelpBox(Styles.cullingMaskWarning.text, MessageType.Info);
            }
        }
#endregion

#region Shadows
        public static CED.IDrawer ShadowsContent()
        {
            return CED.FoldoutGroup(
                LightUI.Styles.shadowHeader,
                Expandable.Shadows,
                expandedState,
                DrawShadowsContent
            );
        }


        private static void DrawShadowsContent(HNRenderPipelineSerializedLight p, UnityEditor.Editor owner)
        {
            if (p.settings.lightType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit shadows from different light types.", MessageType.Info);
                return;
            }

            p.settings.DrawShadowsType();

            if (p.settings.shadowsType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit different shadow types", MessageType.Info);
                return;
            }

            if (p.settings.light.shadows == LightShadows.None)
                return;

            var lightType = p.settings.light.type;

            using (new EditorGUI.IndentLevelScope())
            {
                if (p.settings.isBakedOrMixed)
                {
                    switch (lightType)
                    {
                        // Baked Shadow radius
                        case LightType.Point:
                        case LightType.Spot:
                            p.settings.DrawBakedShadowRadius();
                            break;
                        case LightType.Directional:
                            p.settings.DrawBakedShadowAngle();
                            break;
                    }
                }

//                 if (lightType != LightType.Rectangle && !p.settings.isCompletelyBaked)
//                 {
//                     EditorGUILayout.LabelField(Styles.ShadowRealtimeSettings, EditorStyles.boldLabel);
//                     using (new EditorGUI.IndentLevelScope())
//                     {
//                         // Resolution
//                         if (lightType == LightType.Point || lightType == LightType.Spot)
//                             DrawShadowsResolutionGUI(p);

//                         EditorGUILayout.Slider(p.settings.shadowsStrength, 0f, 1f, Styles.ShadowStrength);

//                         // Bias
//                         DrawAdditionalShadowData(p, owner);

//                         // 该近平面最小边界应与 SharedLightData::GetNearPlaneMinBound() 中的计算保持一致
//                         float nearPlaneMinBound = Mathf.Min(0.01f * p.settings.range.floatValue, 0.1f);
//                         EditorGUILayout.Slider(p.settings.shadowsNearPlane, nearPlaneMinBound, 10.0f, Styles.ShadowNearPlane);
//                         var isHololens = false;
//                         var isQuest = false;
// #if XR_MANAGEMENT_4_0_1_OR_NEWER
//                         var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
//                         var buildTargetSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
//                         if (buildTargetSettings != null && buildTargetSettings.AssignedSettings != null && buildTargetSettings.AssignedSettings.activeLoaders.Count > 0)
//                         {
//                             isHololens = buildTargetGroup == BuildTargetGroup.WSA;
//                             isQuest = buildTargetGroup == BuildTargetGroup.Android;
//                         }

// #endif
//                         // Soft Shadow Quality
//                         if (p.settings.light.shadows == LightShadows.Soft)
//                             EditorGUILayout.PropertyField(p.softShadowQualityProp, Styles.SoftShadowQuality);

//                         if (isHololens || isQuest)
//                         {
//                             EditorGUILayout.HelpBox(
//                                 "Per-light soft shadow quality level is not supported on untethered XR platforms. Use the Soft Shadow Quality setting in the URP Asset instead",
//                                 MessageType.Warning
//                             );
//                         }

//                     }

//                     EditorGUI.BeginChangeCheck();
//                     EditorGUILayout.PropertyField(p.customShadowLayers, Styles.customShadowLayers);
//                     // 撤销 Light 组件上的更改，因为勾选关联后 SyncLightAndShadowLayers 会自动改值
//                     if (EditorGUI.EndChangeCheck())
//                     {
//                         if (p.customShadowLayers.boolValue)
//                         {
//                             p.settings.light.renderingLayerMask = p.shadowRenderingLayers.intValue;
//                         }
//                         else
//                         {
//                             p.serializedAdditionalDataObject.ApplyModifiedProperties(); // 需把上述修改推送到对象上，因为它用于同步
//                             SyncLightAndShadowLayers(p, p.renderingLayers);
//                         }
//                     }

//                     if (p.customShadowLayers.boolValue)
//                     {
//                         using (new EditorGUI.IndentLevelScope())
//                         {
//                             EditorGUI.BeginChangeCheck();
//                             HNRenderPipelineEditorUtils.DrawRenderingLayerMask(p.shadowRenderingLayers, Styles.ShadowLayer);
//                             if (EditorGUI.EndChangeCheck())
//                             {
//                                 p.settings.light.renderingLayerMask = p.shadowRenderingLayers.intValue;
//                                 p.Apply();
//                             }
//                         }
//                     }
//                 }
            }

            // if (!UnityEditor.Lightmapping.bakedGI && !p.settings.lightmapping.hasMultipleDifferentValues && p.settings.isBakedOrMixed)
            //     EditorGUILayout.HelpBox(Styles.BakingWarning.text, MessageType.Warning);
        }
#endregion

        private static readonly ExpandedState<Expandable, Light> expandedState = new ExpandedState<Expandable, Light>(Expandable.General);

        public enum Expandable
        {
            General = 1 << 0,
            Shape = 1 << 1,
            Emission = 1 << 2,
            Rendering = 1 << 3,
            Shadows = 1 << 4,
            LightCookie = 1 << 5
        }


        public class Styles
        {
            public static readonly GUIContent type = EditorGUIUtility.TrTextContent("Type", "Specifies the current type of light. Possible types are Directional, Spot, Point, and Area lights.");
            
            public static readonly GUIContent areaLightShapeContent = EditorGUIUtility.TrTextContent("Shape", "Specifies the shape of the area light.");
            public static readonly GUIContent[] lightTypeTitles = { EditorGUIUtility.TrTextContent("Directional"), EditorGUIUtility.TrTextContent("Point"), EditorGUIUtility.TrTextContent("Spot"), EditorGUIUtility.TrTextContent("Area") };
            public static readonly int[] lightTypeValues = { (int)LightType.Directional, (int)LightType.Point, (int)LightType.Spot, (int)LightType.Area };
            public static readonly GUIContent LightCookieSize = EditorGUIUtility.TrTextContent("Cookie Size", "Controls the size of the cookie mask currently assigned to the light.");
            public static readonly GUIContent LightCookieOffset = EditorGUIUtility.TrTextContent("Cookie Offset", "Controls the offset of the cookie mask currently assigned to the light.");
            public static readonly GUIContent renderingLayers = EditorGUIUtility.TrTextContent("Rendering Layers", "Select the Rendering Layers that the Light affects. This Light affects objects where at least one Rendering Layer value matches.");
            public static readonly GUIContent cullingMask = EditorGUIUtility.TrTextContent("Culling Mask", "Specifies which lights are culled per camera. To control exclude certain lights affecting certain objects, use Rendering Layers instead, which is supported across all rendering paths.");

            public static readonly GUIContent[] areaLightShapeTitles = { EditorGUIUtility.TrTextContent("Rectangle"), EditorGUIUtility.TrTextContent("Disc") };
            public static readonly int[] areaLightShapeValues = { (int)LightType.Rectangle, (int)LightType.Disc };
            
            public static readonly GUIContent sunSourceWarning = EditorGUIUtility.TrTextContent("This light is set as the current Sun Source, which requires a directional light. Go to the Lighting Window's Environment settings to edit the Sun Source.");
            public static readonly GUIContent cullingMaskWarning = EditorGUIUtility.TrTextContent("Culling Mask should be used to control which lights are culled per camera. If you want to exclude certain lights from affecting certain objects, use Rendering Layers on the Light, and Rendering Layer Mask on the Mesh Renderer.");
        }
    }
}
