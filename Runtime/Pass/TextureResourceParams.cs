// <copyright file="TextureResourceParams.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 纹理资源参数（值类型）。承载在渲染图中分配一张纹理所需的全部参数，
    /// 作为 Pass 的 <c>[SerializeField]</c> 字段序列化（取代旧的
    /// <see cref="ResourceDefinition"/> 体系）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当 Pass 输入槽未连接或句柄无效时，Pass 内部用这些参数自建纹理
    /// （<c>renderGraph.CreateTexture</c>）。参数只描述分配路径；外部纹理导入
    /// 已随资源节点体系一并移除——唯一使用场景（Reflection 图 emptyTexture）
    /// 经验证不产生任何渲染效果。
    /// </para>
    /// </remarks>
    [Serializable]
    public struct TextureResourceParams
    {
        /// <summary>
        /// 分配纹理的颜色格式。深度纹理忽略此字段（改由 <see cref="DepthBits"/> 控制）。
        /// </summary>
        public GraphicsFormat ColorFormat;

        /// <summary>
        /// 深度缓冲位数。<see cref="DepthBits.None"/> 表示非深度纹理。
        /// </summary>
        public DepthBits DepthBits;

        /// <summary>
        /// Texture Array Slices 数。
        /// </summary>
        public int Slices;

        /// <summary>
        /// 相对相机像素尺寸的缩放系数。默认全分辨率。
        /// 当 <see cref="Width"/> 与 <see cref="Height"/> 均为正时被忽略（固定尺寸模式）。
        /// </summary>
        public Vector2 TextureScale;

        /// <summary>
        /// 固定纹理宽度（像素）。与 <see cref="Height"/> 同时为正时使用固定尺寸，
        /// 否则按相机缩放。默认 0（相机缩放模式）。
        /// </summary>
        public int Width;

        /// <summary>
        /// 固定纹理高度（像素）。与 <see cref="Width"/> 同时为正时使用固定尺寸。
        /// 默认 0。
        /// </summary>
        public int Height;

        /// <summary>
        /// 采样过滤模式。
        /// </summary>
        public FilterMode FilterMode;

        /// <summary>
        /// UV 包裹模式。
        /// </summary>
        public TextureWrapMode WrapMode;

        /// <summary>
        /// 纹理维度。
        /// </summary>
        public TextureDimension TextureDimension;

        /// <summary>
        /// 是否生成 mipmap。默认 <c>false</c>。
        /// </summary>
        public bool UseMipMap;

        /// <summary>
        /// 是否自动生成 mipmap。
        /// </summary>
        public bool AutoGenerateMips;

        /// <summary>
        /// 是否在首次使用前清空。清空由渲染图内联到首个写入该资源的 pass。
        /// </summary>
        public bool ClearBuffer;

        /// <summary>
        /// <see cref="ClearBuffer"/> 为 <c>true</c> 时的清空颜色。
        /// </summary>
        public Color ClearColor;

        /// <summary>
        /// 初始化默认参数：全分辨率 LDR、无深度、Bilinear 过滤、Repeat 包裹、
        /// Tex2D、无 mip、清空为黑色。
        /// </summary>
        public static TextureResourceParams CreateDefault()
        {
            return new TextureResourceParams
            {
                ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                DepthBits = DepthBits.None,
                Slices = 1,
                TextureScale = Vector2.one,
                FilterMode = FilterMode.Bilinear,
                WrapMode = TextureWrapMode.Repeat,
                TextureDimension = TextureDimension.Tex2D,
                ClearBuffer = true,
                ClearColor = Color.black,
            };
        }

        /// <summary>
        /// 构建渲染图纹理描述符。
        /// </summary>
        /// <param name="name">纹理名（用于渲染图调试显示）。</param>
        /// <param name="camera">当前相机，用于相机缩放尺寸模式。</param>
        /// <returns>可直接传给 <c>renderGraph.CreateTexture</c> 的描述符。</returns>
        public TextureDesc CreateDesc(string name, Camera camera)
        {
            int texWidth = Width > 0
                ? Width
                : Mathf.Max(1, Mathf.RoundToInt(camera.pixelWidth * TextureScale.x));
            int texHeight = Height > 0
                ? Height
                : Mathf.Max(1, Mathf.RoundToInt(camera.pixelHeight * TextureScale.y));

            return new TextureDesc(texWidth, texHeight, false, false)
            {
                colorFormat = ColorFormat,
                depthBufferBits = DepthBits,
                slices = Slices,
                filterMode = FilterMode,
                wrapMode = WrapMode,
                dimension = TextureDimension,
                clearBuffer = ClearBuffer,
                clearColor = ClearColor,
                useMipMap = UseMipMap,
                autoGenerateMips = AutoGenerateMips,
                name = name,
            };
        }
    }
}
