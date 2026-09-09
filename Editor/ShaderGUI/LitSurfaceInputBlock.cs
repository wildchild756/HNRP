using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace HN.HNRP.Editor
{
    public class LitSurfaceInputBlock : MaterialGUIBlock
    {
        public LitSurfaceInputBlock(uint expandableBit) : base(expandableBit)
        {
            header = new GUIContent("Surface Inputs");
        }

        protected override void GetProperties(MaterialProperty[] properties)
        {
            baseMapProperty = GetProperty(properties, MaterialPropertys.baseMap);
            baseColorProperty = GetProperty(properties, MaterialPropertys.baseColor);
            alphaRemapMinProperty = GetProperty(properties, MaterialPropertys.alphaRemapMin);
            alphaRemapMaxProperty = GetProperty(properties, MaterialPropertys.alphaRemapMax);
            maskMapProperty = GetProperty(properties, MaterialPropertys.maskMap);
            metallicRemapMinProperty = GetProperty(properties, MaterialPropertys.metallicRemapMin);
            metallicRemapMaxProperty = GetProperty(properties, MaterialPropertys.metallicRemapMax);
            smoothnessRemapMinProperty = GetProperty(properties, MaterialPropertys.smoothnessRemapMin);
            smoothnessRemapMaxProperty = GetProperty(properties, MaterialPropertys.smoothnessRemapMax);
            aoRemapMinProperty = GetProperty(properties, MaterialPropertys.aoRemapMin);
            aoRemapMaxProperty = GetProperty(properties, MaterialPropertys.aoRemapMax);
            metallicProperty = GetProperty(properties, MaterialPropertys.metallic);
            smoothnessProperty = GetProperty(properties, MaterialPropertys.smoothness);
            normalMapProperty = GetProperty(properties, MaterialPropertys.normalMap);
            normalScaleProperty = GetProperty(properties, MaterialPropertys.normalScale);
            emissionMapProperty = GetProperty(properties, MaterialPropertys.emissionMap);
            emissionColorProperty = GetProperty(properties, MaterialPropertys.emissionColor);
        }

        protected override void DrawGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            // 基础贴图
            DrawTextureAndColor(materialEditor, baseMapProperty, baseColorProperty, Styles.baseMap);

            // Alpha
            MaterialGUI.SurfaceType blendMode = (MaterialGUI.SurfaceType)(materialEditor.target as Material).GetFloat(MaterialPropertys.surfaceType);
            if (baseMapProperty != null && baseMapProperty.textureValue != null && blendMode == MaterialGUI.SurfaceType.Transparent)
            {
                DrawMinMaxSlider(materialEditor, alphaRemapMinProperty, alphaRemapMaxProperty, 0.0f, 1.0f, Styles.alphaRemapping);
            }

            // 平滑度 / 金属度
            if (maskMapProperty == null || maskMapProperty.textureValue == null)
            {
                DrawSlider(materialEditor, smoothnessProperty, Styles.smoothness);
                DrawSlider(materialEditor, metallicProperty, Styles.metallic);
            }

            EditorGUILayout.Space();

            // 掩码贴图
            DrawTexture(materialEditor, maskMapProperty, Styles.maskMap);

            // SmoothnessRemap / MetallicRemap / AORemap
            if (maskMapProperty != null && maskMapProperty.textureValue != null)
            {
                DrawMinMaxSlider(materialEditor, smoothnessRemapMinProperty, smoothnessRemapMaxProperty, 0.0f, 1.0f, Styles.smoothnessRemapping);
                DrawMinMaxSlider(materialEditor, metallicRemapMinProperty, metallicRemapMaxProperty, 0.0f, 1.0f, Styles.metallicRemapping);
                DrawMinMaxSlider(materialEditor, aoRemapMinProperty, aoRemapMaxProperty, 0.0f, 1.0f, Styles.aoRemapping);
            }

            // 法线贴图
            DrawTextureAndSlider(materialEditor, normalMapProperty, normalScaleProperty, Styles.normalMap);

            // 自发光贴图
            DrawTextureAndColor(materialEditor, emissionMapProperty, emissionColorProperty, Styles.emissionMap);

            // 基础贴图缩放偏移
            DrawTextureScaleOffset(materialEditor, baseMapProperty);
        }

        public override void OnValidateMaterial(Material material)
        {
            SetMaterialKeywords(material);
        }

        protected void SetMaterialKeywords(Material material)
        {
            SetKeywordByTexture(material, MaterialPropertys.baseMap, MaterialLitKeywords.basemap);    
            SetKeywordByTexture(material, MaterialPropertys.normalMap, MaterialLitKeywords.normalMap);
            SetKeywordByTexture(material, MaterialPropertys.maskMap, MaterialLitKeywords.maskMap);
            SetKeywordByTexture(material, MaterialPropertys.emissionMap, MaterialLitKeywords.emissionMap);
        }


        protected MaterialProperty baseMapProperty = null;
        protected MaterialProperty baseColorProperty = null;
        protected MaterialProperty alphaRemapMinProperty = null;
        protected MaterialProperty alphaRemapMaxProperty = null;
        protected MaterialProperty maskMapProperty = null;
        protected MaterialProperty metallicRemapMinProperty = null;
        protected MaterialProperty metallicRemapMaxProperty = null;
        protected MaterialProperty smoothnessRemapMinProperty = null;
        protected MaterialProperty smoothnessRemapMaxProperty = null;
        protected MaterialProperty aoRemapMinProperty = null;
        protected MaterialProperty aoRemapMaxProperty = null;
        protected MaterialProperty metallicProperty = null;
        protected MaterialProperty smoothnessProperty = null;
        protected MaterialProperty normalMapProperty = null;
        protected MaterialProperty normalScaleProperty = null;
        protected MaterialProperty emissionMapProperty = null;
        protected MaterialProperty emissionColorProperty = null;


        protected static class Styles
        {
            public static readonly GUIContent baseMap = EditorGUIUtility.TrTextContent("Base Map", "Color(RGB) Alpha(A)");
            public static readonly GUIContent alphaRemapping = EditorGUIUtility.TrTextContent("Alpha Remapping", "(0, 1)");
            public static readonly GUIContent maskMap = EditorGUIUtility.TrTextContent("Mask Map", "Smoothness(R) Metallic(G) Occlusion(B) DetailMask(A)");
            public static readonly GUIContent metallicRemapping = EditorGUIUtility.TrTextContent("Metallic Remap", "(0, 1)");
            public static readonly GUIContent smoothnessRemapping = EditorGUIUtility.TrTextContent("Smoothness Remap", "(0, 1)");
            public static readonly GUIContent aoRemapping = EditorGUIUtility.TrTextContent("AO Remap", "(0, 1)");
            public static readonly GUIContent metallic = EditorGUIUtility.TrTextContent("Metallic", "(0, 1)");
            public static readonly GUIContent smoothness = EditorGUIUtility.TrTextContent("Smoothness", "(0, 1)");
            public static readonly GUIContent normalMap = EditorGUIUtility.TrTextContent("Normal Map", "(0, 8)");
            public static readonly GUIContent emissionMap = EditorGUIUtility.TrTextContent("Emission Map", "");
        }


    }
}
