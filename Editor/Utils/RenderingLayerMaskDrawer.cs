using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace HN.HNRP.Editor
{
    [CustomPropertyDrawer(typeof(RenderingLayerMaskAttribute))]
    public class RenderingLayerMaskDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect controlRect = EditorGUILayout.GetControlRect(true);
            int renderingLayer = property.intValue;

            string[] renderingLayerMaskNames = HNRenderPipelineGlobalSettings.Instance.RenderingLayerNames;
            int maskCount = (int)Mathf.Log(renderingLayer, 2) + 1;
            if (renderingLayerMaskNames.Length < maskCount && maskCount <= 32)
            {
                var newRenderingLayerMaskNames = new string[maskCount];
                for (int i = 0; i < maskCount; ++i)
                {
                    newRenderingLayerMaskNames[i] = i < renderingLayerMaskNames.Length ? renderingLayerMaskNames[i] : $"Unused Layer {i}";
                }
                renderingLayerMaskNames = newRenderingLayerMaskNames;

                EditorGUILayout.HelpBox($"One or more of the Rendering Layers is not defined in the Universal Global Settings asset.", MessageType.Warning);
            }

            EditorGUI.BeginProperty(controlRect, label, property);

            EditorGUI.BeginChangeCheck();
            renderingLayer = EditorGUI.MaskField(controlRect, label, renderingLayer, renderingLayerMaskNames);

            if (EditorGUI.EndChangeCheck())
                property.uintValue = (uint)renderingLayer;

            EditorGUI.EndProperty();
        }
    }
}
