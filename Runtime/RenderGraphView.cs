using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RenderGraphViews
{
    public List<RenderGraphView> renderGraphViews;


    public RenderGraphViews()
    {
        renderGraphViews = new List<RenderGraphView>();
        for (int i = 0; i < DefaultViews.Length; i++)
        {
            CreateView(DefaultViews[i]);
        }
    }

    public bool ContainsView(string viewName)
    {
        for (int i = 0; i < renderGraphViews.Count; i++)
        {
            if (renderGraphViews[i].Name == viewName)
            {
                return true;
            }
        }
        return false;
    }

    public void CreateView(string viewName)
    {
        if (ContainsView(viewName))
        {
            Debug.LogError($"Render Graph View {viewName} already exists.");
            return;
        }

        renderGraphViews.Add(new RenderGraphView(viewName));
    }


    public static string[] DefaultViews = new string[]
    {
            "MainGameView"
    };
}


[Serializable]
public class RenderGraphView
{
    [SerializeField]
    public string Name => name;
    private string name;

    [SerializeField]
    public HNRenderGraph RenderGraph => renderGraph;
    private HNRenderGraph renderGraph;


    public RenderGraphView(string name)
    {
        this.name = name;
    }

    public void SetRenderGraph(HNRenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public void CleanView()
    {
        this.renderGraph = null;
    }
}
