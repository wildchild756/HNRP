// <copyright file="DrawObjectPassEditor.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// Inspector Editor for <see cref="DrawObjectPass"/>.
    /// Draws the pass-owned resource parameters (color / depth target allocation,
    /// renderer list) and the light-globals option, and defines the pass presets
    /// offered in the <see cref="RenderGraphAsset"/> inspector.
    /// </summary>
    [PassEditor(typeof(DrawObjectPass))]
    public class DrawObjectPassEditor : PassEditor
    {
        /// <inheritdoc />
        public override IReadOnlyList<IPassPreset> Presets => s_Presets;

        /// <inheritdoc />
        protected override void DrawParameters(SerializedProperty passProp, Pass pass)
        {
            var s = passProp.FindPropertyRelative("ColorTargetSlot");
            Debug.Log(s);
            SerializedProperty colorTarget = passProp.FindPropertyRelative("colorTargetParams");
            if (colorTarget != null)
            {
                EditorGUILayout.PropertyField(colorTarget, new GUIContent(
                    "Color Target",
                    "输入槽未连接时本地分配的颜色缓冲参数。"));
            }

            SerializedProperty depthTarget = passProp.FindPropertyRelative("depthTargetParams");
            if (depthTarget != null)
            {
                EditorGUILayout.PropertyField(depthTarget, new GUIContent(
                    "Depth Target",
                    "输入槽未连接时本地分配的深度缓冲参数。"));
            }

            SerializedProperty rendererList = passProp.FindPropertyRelative("rendererListParams");
            if (rendererList != null)
            {
                EditorGUILayout.PropertyField(rendererList, new GUIContent(
                    "Renderer List",
                    "输入槽未连接时本地构建的渲染器列表参数（队列范围 / 渲染层掩码）。"));
            }

            SerializedProperty setLightGlobals = passProp.FindPropertyRelative("setLightGlobals");
            if (setLightGlobals != null)
            {
                EditorGUILayout.PropertyField(setLightGlobals, new GUIContent(
                    "Set Light Globals",
                    "绘制前设置 probe / light / light-data shader 全局。无 cluster culling 数据的图（如预览）应关闭。"));
            }
        }

        /// <summary>
        /// 内置预设集合。新增预设：加一个静态字段并追加到此数组。
        /// </summary>
        private static readonly IPassPreset[] s_Presets =
        {
            new PassPreset<DrawObjectPass>("Default Opaque", new DrawObjectPass()),
            new PassPreset<DrawObjectPass>("Transparent", new DrawObjectPass
            {
                RendererListParams = new RendererListParams
                {
                    ListKind = RenderListKind.Transparent,
                    RenderingLayerMask = 0x00000001,
                },
            }),
        };
    }
}
