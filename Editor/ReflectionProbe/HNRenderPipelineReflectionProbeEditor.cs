using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.Rendering;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP.Editor
{
    [CustomEditorForRenderPipeline(typeof(ReflectionProbe), typeof(HNRenderPipelineAsset))]
    [CanEditMultipleObjects]
    public class HNRenderPipelineReflectionProbeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var rpAsset = GraphicsSettings.currentRenderPipeline as HNRenderPipelineAsset;
            if(rpAsset == null)
            {
                base.OnInspectorGUI();
                return;    
            }
            
            serializedReflectionProbe.Update();
            EditorGUI.BeginChangeCheck();

            HNRenderPipelineReflectionProbeUI.DrawToolBarAndHeaderSettings(serializedReflectionProbe, this);
            if (EditMode.editMode == EditMode.SceneViewEditMode.ReflectionProbeOrigin)
            {
                UpdateOldLocalSpace();
            }

            var inspector = HNRenderPipelineReflectionProbeUI.Inspector();
            inspector.Draw(serializedReflectionProbe, this);
            
            if(EditorGUI.EndChangeCheck())
            {
                lastInteractedEditor = this;
                serializedReflectionProbe.Apply();
            }
        }

        public void OnEnable()
        {
            serializedReflectionProbe = new HNRenderPipelineSerializedReflectionProbe(serializedObject);

            reflectionProbe.GetHNAdditionalReflectionProbeData();

            Undo.undoRedoPerformed += ReconstructReferenceToAdditionalDataSO;
        }

        public void OnDisable()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                currentlyEditedProbes.Add((ReflectionProbe)targets[i]);
            }
            
            Undo.undoRedoPerformed -= ReconstructReferenceToAdditionalDataSO;
        }

        public void OnSceneGUI()
        {
            if (sceneViewEditing)
            {
                switch (EditMode.editMode)
                {
                    case EditMode.SceneViewEditMode.ReflectionProbeBox:
                        DoBoxEditing();
                        break;
                    case EditMode.SceneViewEditMode.ReflectionProbeOrigin:
                        DoOriginEditing();
                        break;
                }
            }
        }
                

        private void ReconstructReferenceToAdditionalDataSO()
        {
            OnDisable();
            OnEnable();
        }

        private void DoBoxEditing()
        {
            ReflectionProbe reflectionProbe = (ReflectionProbe)target;
            using (new Handles.DrawingScope(HNRenderPipelineReflectionProbeUI.GetLocalSpace(reflectionProbe)))
            {
                boundsHandle.center = reflectionProbe.center;
                boundsHandle.size = reflectionProbe.size;
                EditorGUI.BeginChangeCheck();
                boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(reflectionProbe, "Modified Reflection Probe AABB");
                    Vector3 center = boundsHandle.center;
                    Vector3 size = boundsHandle.size;
                    HNRenderPipelineReflectionProbeUI.ValidateAABB(reflectionProbe, ref center, ref size);
                    reflectionProbe.center = center;
                    reflectionProbe.size = size;
                    EditorUtility.SetDirty(target);
                }
            }
        }

        private void DoOriginEditing()
        {
            ReflectionProbe reflectionProbe = (ReflectionProbe)target;
            Vector3 position = reflectionProbe.transform.position;
            Vector3 size = reflectionProbe.size;
            EditorGUI.BeginChangeCheck();
            Vector3 point = Handles.PositionHandle(position, HNRenderPipelineReflectionProbeUI.GetLocalSpaceRotation(reflectionProbe));
            if (EditorGUI.EndChangeCheck() || oldLocalSpace != HNRenderPipelineReflectionProbeUI.GetLocalSpace((ReflectionProbe)target))
            {
                Vector3 point2 = oldLocalSpace.inverse.MultiplyPoint3x4(point);
                point2 = new Bounds(reflectionProbe.center, size).ClosestPoint(point2);
                Undo.RecordObject(reflectionProbe.transform, "Modified Reflection Probe Origin");
                reflectionProbe.transform.position = oldLocalSpace.MultiplyPoint3x4(point2);
                Undo.RecordObject(reflectionProbe, "Modified Reflection Probe Origin");
                reflectionProbe.center = HNRenderPipelineReflectionProbeUI.GetLocalSpace(reflectionProbe).inverse.MultiplyPoint3x4(oldLocalSpace.MultiplyPoint3x4(reflectionProbe.center));
                EditorUtility.SetDirty(target);
                UpdateOldLocalSpace();
            }
        }

        private void UpdateOldLocalSpace()
        {
            oldLocalSpace = HNRenderPipelineReflectionProbeUI.GetLocalSpace((ReflectionProbe)target);
        }
        

        [DrawGizmo(GizmoType.Active)]
        private static void RenderBoxGizmo(ReflectionProbe reflectionProbe, GizmoType gizmoType)
        {
            if (!(lastInteractedEditor == null) && lastInteractedEditor.sceneViewEditing && EditMode.editMode == EditMode.SceneViewEditMode.ReflectionProbeBox)
            {
                Color color = Gizmos.color;
                Gizmos.color = kGizmoReflectionProbe;
                Gizmos.matrix = HNRenderPipelineReflectionProbeUI.GetLocalSpace(reflectionProbe);
                Gizmos.DrawCube(reflectionProbe.center, -1f * reflectionProbe.size);
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = color;
            }
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void RenderBoxOutline(ReflectionProbe reflectionProbe, GizmoType gizmoType)
        {
            if (currentlyEditedProbes.Contains(reflectionProbe))
            {
                Color color = Gizmos.color;
                Gizmos.color = (reflectionProbe.isActiveAndEnabled ? kGizmoReflectionProbe : kGizmoReflectionProbeDisabled);
                Gizmos.matrix = HNRenderPipelineReflectionProbeUI.GetLocalSpace(reflectionProbe);
                Gizmos.DrawWireCube(reflectionProbe.center, reflectionProbe.size);
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = color;
            }
        }


        internal readonly AnimBool showProbeModeRealtimeOptions = new AnimBool();
        internal readonly AnimBool showProbeModeCustomOptions = new AnimBool();
        internal readonly AnimBool showProbeModeBakedOptions = new AnimBool();

        private ReflectionProbe reflectionProbe => target as ReflectionProbe;
        private HNRenderPipelineSerializedReflectionProbe serializedReflectionProbe;
        private bool sceneViewEditing => HNRenderPipelineReflectionProbeUI.IsReflectionProbeEditMode(EditMode.editMode) && EditMode.IsOwner(this);
        private BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
        private Matrix4x4 oldLocalSpace = Matrix4x4.identity;

        internal static Color kGizmoReflectionProbe = new Color(1f, 0.8980392f, 0.5803922f, 0.5019608f);
        internal static Color kGizmoReflectionProbeDisabled = new Color(0.6f, 0.5372549f, 0.34901962f, 32f / 85f);

        private static HNRenderPipelineReflectionProbeEditor lastInteractedEditor;
        private static HashSet<ReflectionProbe> currentlyEditedProbes = new HashSet<ReflectionProbe>();
    }
}
