using System.Collections;
using System.Collections.Generic;
using HN.Graph;
using UnityEngine;

[HNRenderGraphNodeInfo("Draw Opaque Pass", "Pass/Draw Opaque Pass")]
public class DrawOpaquePass : HNRenderPass
{
    [HNRenderGraphPortInfo("Test Input RT 0", HNGraphPortInfoAttribute.SlotType.Input)]
    public RenderTexture TestInputRT0 => testInputRT0;
    private RenderTexture testInputRT0;
    [HNRenderGraphPortInfo("Test Input RT 1", HNGraphPortInfoAttribute.SlotType.Input)]
    public RenderTexture TestInputRT1 => testInputRT1;
    private RenderTexture testInputRT1;
    [HNRenderGraphPortInfo("Test Output RT 0", HNGraphPortInfoAttribute.SlotType.Output)]
    public RenderTexture TestOutputRT0 => testOutputRT0;
    private RenderTexture testOutputRT0;
    public DrawOpaquePass()
    {
        Initialize();

        passName = "Draw Opaque";
    }

}

