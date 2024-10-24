using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class HNRenderPipelineResources
{
    public static HNRenderPipelineResources Instance;


    public HNRenderGraph defaultRenderGraph;
    

    public HNRenderPipelineResources()
    {
        AssetDatabase.CreateAsset(defaultRenderGraph, defaultGraphPath);

        Instance = this;
    }


    public static string defaultGraphPath = "";
}
