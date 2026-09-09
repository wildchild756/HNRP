using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor;

namespace HN.HNRP.Editor
{
    public class HNRenderPipelineSerializedCamera : ISerializedCamera
    {
        public HNRenderPipelineSerializedCamera(SerializedObject serializedObject, CameraEditor.Settings settings)
        {
            this.serializedObject = serializedObject;
            projectionMatrixMode = serializedObject.FindProperty("m_projectionMatrixMode");

            allowDynamicResolution = serializedObject.FindProperty("m_AllowDynamicResolution");

            if (settings == null)
            {
                baseCameraSettings = new CameraEditor.Settings(serializedObject);
                baseCameraSettings.OnEnable();
            }
            else
            {
                baseCameraSettings = settings;
            }

            var camerasAdditionalData = CoreEditorUtils.GetAdditionalData<HNAdditionalCameraData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(camerasAdditionalData);

            // 通用属性
            stopNaNs = serializedAdditionalDataObject.FindProperty("stopNaNs");
            dithering = serializedAdditionalDataObject.FindProperty("dithering");
            // antialiasing = serializedAdditionalDataObject.FindProperty("m_Antialiasing");
            volumeLayerMask = serializedAdditionalDataObject.FindProperty("volumeLayerMask");
            clearDepth = serializedAdditionalDataObject.FindProperty("clearDepth");

            // HNRP 专属属性
            renderGraphViewIndex = serializedAdditionalDataObject.FindProperty("renderGraphViewIndex");
        }

        public void Apply()
        {
            baseCameraSettings.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
        }

        public void Refresh()
        {

        }

        public void Update()
        {
            baseCameraSettings.Update();
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
        }
        

        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }

        public CameraEditor.Settings baseCameraSettings { get; }

        public SerializedProperty projectionMatrixMode { get; }

        // 通用属性
        public SerializedProperty dithering { get; }
        public SerializedProperty stopNaNs { get; }
        public SerializedProperty allowDynamicResolution { get; }
        public SerializedProperty volumeLayerMask { get; }
        public SerializedProperty clearDepth { get; }
        public SerializedProperty antialiasing { get; }

        // HNRP 专属属性
        public SerializedProperty renderGraphViewIndex { get; }

    }
}
