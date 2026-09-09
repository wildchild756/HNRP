using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;

namespace HN.HNRP.Editor
{
    public interface ISerializedReflectionProbe
    {
        SerializedObject serializedObject { get; }
        SerializedObject serializedAdditionalDataObject { get; }

        SerializedProperty mode { get; }
        SerializedProperty importance { get; }
        SerializedProperty intensity { get; }

        void Update();
        void Apply();
    }


    public class HNRenderPipelineSerializedReflectionProbe : ISerializedReflectionProbe
    {
        public HNRenderPipelineSerializedReflectionProbe(SerializedObject serializedObject)
        {
            this.serializedObject = serializedObject;

            var reflectionProbeAdditionalData = CoreEditorUtils.GetAdditionalData<HNAdditionalReflectionProbeData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(reflectionProbeAdditionalData);

            mode = serializedObject.FindProperty("m_Mode");
            refreshMode = serializedObject.FindProperty("m_RefreshMode");
            timeSlicingMode = serializedObject.FindProperty("m_TimeSlicingMode");
            renderDynamicObjects = serializedObject.FindProperty("m_RenderDynamicObjects");
            customBakedTexture = serializedObject.FindProperty("m_CustomBakedTexture");
            importance = serializedObject.FindProperty("m_Importance");
            intensity = serializedObject.FindProperty("m_IntensityMultiplier");
            boxProjection = serializedObject.FindProperty("m_BoxProjection");
            blendDistance = serializedObject.FindProperty("m_BlendDistance");
            boxSize = serializedObject.FindProperty("m_BoxSize");
            boxOffset = serializedObject.FindProperty("m_BoxOffset");
            resolution = serializedObject.FindProperty("m_Resolution");
            hdr = serializedObject.FindProperty("m_HDR");
            shadowDistance = serializedObject.FindProperty("m_ShadowDistance");
            clearFlag = serializedObject.FindProperty("m_ClearFlags");
            backGroundColor = serializedObject.FindProperty("m_BackGroundColor");
            cullingMask = serializedObject.FindProperty("m_CullingMask");
            occlusionCulling = serializedObject.FindProperty("m_UseOcclusionCulling");
            nearAndFarClipingPlanes = new SerializedProperty[2]
            {
                serializedObject.FindProperty("m_NearClip"),
                serializedObject.FindProperty("m_FarClip")
            };

            // HNRP 专属属性
            renderGraphViewIndex = serializedAdditionalDataObject.FindProperty("renderGraphViewIndex");
        }

        public void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
        }

        public void Update()
        {
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
        }


        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }
        
        public SerializedProperty mode { get; }
        public SerializedProperty refreshMode { get; }
        public SerializedProperty timeSlicingMode { get; }
        public SerializedProperty renderDynamicObjects { get; }
        public SerializedProperty customBakedTexture { get; }
        public SerializedProperty importance { get; }
        public SerializedProperty intensity { get; }
        public SerializedProperty boxProjection { get; }
        public SerializedProperty blendDistance { get; }
        public SerializedProperty boxSize { get; }
        public SerializedProperty boxOffset { get; }
        public SerializedProperty resolution { get; }
        public SerializedProperty hdr { get; }
        public SerializedProperty shadowDistance { get; }
        public SerializedProperty clearFlag { get; }
        public SerializedProperty backGroundColor { get; }
        public SerializedProperty cullingMask { get; }
        public SerializedProperty occlusionCulling { get; }
        public SerializedProperty[] nearAndFarClipingPlanes { get; }

        // HNRP 专属属性
        public SerializedProperty renderGraphViewIndex { get; }

    }
}
