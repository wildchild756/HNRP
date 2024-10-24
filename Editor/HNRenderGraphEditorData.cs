using System.Collections;
using System.Collections.Generic;
using HN.Graph.Editor;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;

public class HNRenderGraphEditorData : HNGraphEditorData
{



    [MenuItem("Assets/Create/Rendering/HN Render Graph")]
    public static void CreateRenderGraph()
    {
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
            0,
            ScriptableObject.CreateInstance<HNRenderGraphNewAction>(),
            string.Format("New HN Render Graph.{0}", HNRenderGraph.HNRenderGraphExtension),
            null,
            null);
    }
}


[ScriptedImporter(1, HNRenderGraph.HNRenderGraphExtension)]
public class HNRenderGraphImporter : HNGraphImporter<HNRenderGraph>
{

}


[CustomEditor(typeof(HNRenderGraphImporter))]
public class HNRenderGraphImporterEditor : HNGraphImporterEditor<HNRenderGraph>
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }


    [OnOpenAsset(0)]
    public static bool OnOpenAsset(int instanceID, int line)
    {
        string path = AssetDatabase.GetAssetPath(instanceID);
        HNRenderGraph graphData = AssetDatabase.LoadAssetAtPath<HNRenderGraph>(path);
        HNRenderGraphEditorData graphEditorData = ScriptableObject.CreateInstance<HNRenderGraphEditorData>();
        graphEditorData.Initialize(graphData);
        return OnOpenGraph(path, HNRenderGraph.HNRenderGraphExtension, graphEditorData);
    }
}


public class HNRenderGraphNewAction : HNGraphNewAction<HNRenderGraph>
{

}