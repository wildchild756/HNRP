using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;

namespace HN.HNRP.Editor
{
    public class HNRenderPipelineSerializedLight : ISerializedLight
    {
        public HNRenderPipelineSerializedLight(SerializedObject serializedObject, LightEditor.Settings settings)
        {
            this.serializedObject = serializedObject;

            var lightsAdditionalData = CoreEditorUtils.GetAdditionalData<HNAdditionalLightData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(lightsAdditionalData);

            if (settings == null)
            {
                settings = new LightEditor.Settings(serializedObject);
                settings.OnEnable();
            }
            else
            {
                this.settings = settings;
            }

            intensity = serializedAdditionalDataObject.FindProperty("intensity");

            lightCookieSizeProperty = serializedAdditionalDataObject.FindProperty("cookieSize");
            lightCookieOffsetProperty = serializedAdditionalDataObject.FindProperty("cookieOffset");
            renderingLayerMask = serializedAdditionalDataObject.FindProperty("renderingLayerMask");
            cascadeShadowProperty = serializedAdditionalDataObject.FindProperty("cascadeShadow");
            enableShadowProperty = serializedAdditionalDataObject.FindProperty("enableShadow");
        }

        public void Apply()
        {
            settings.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
        }

        public void Refresh()
        {

        }

        public void Update()
        {
            settings.Update();
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
        }


        public SerializedObject serializedObject { get; private set; }
        public SerializedObject serializedAdditionalDataObject { get; private set; }

        public LightEditor.Settings settings { get; private set; }

        public SerializedProperty intensity { get; private set; }

        public SerializedProperty lightCookieSizeProperty { get; private set; }
        public SerializedProperty lightCookieOffsetProperty { get; private set; }
        public SerializedProperty renderingLayerMask { get; private set; }
        public SerializedProperty cascadeShadowProperty { get; private set; }
        public SerializedProperty enableShadowProperty { get; private set; }
    }
}
