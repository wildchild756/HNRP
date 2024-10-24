using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/HN Rendering Pipeline")]
public class HNRenderPipelineAsset : RenderPipelineAsset
{
    [SerializeField]
    public bool useDynamicBatching;

    [SerializeField]
    public bool useGPUInstancing;

    [SerializeField]
    public bool useSRPBatcher;

    [SerializeField]
    public bool useLightsPerObjectData;

    [SerializeField]
    public RenderGraphViews renderGraphViews;

    [SerializeField]
    public ShadowSettings shadowSettings;

    [SerializeField]
    public PostProcessingSettings postProcessingSettings;


    public HNRenderPipelineAsset()
    {
        useDynamicBatching = false;
        useGPUInstancing = true;
        useSRPBatcher = true;
        useLightsPerObjectData = true;

        renderGraphViews = new RenderGraphViews();
        shadowSettings = default;
        postProcessingSettings = default;
    }

    protected override RenderPipeline CreatePipeline()
    {
        return new HNRenderPipeline();
    }


}
