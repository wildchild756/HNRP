using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using System.Text.RegularExpressions;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HN.HNRP
{
    public static class Blitter
    {
        static Material blit;
        static Material blitTexArray;
        static Material blitColorAndDepth;

        static Mesh triangleMesh;
        static Mesh quadMesh;

        static LocalKeyword decodeHdrKeyword;

        static class BlitShaderIDs
        {
            public static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
            public static readonly int _BlitCubeTexture = Shader.PropertyToID("_BlitCubeTexture");
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitScaleBiasRt = Shader.PropertyToID("_BlitScaleBiasRt");
            public static readonly int _BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");
            public static readonly int _BlitTextureSize = Shader.PropertyToID("_BlitTextureSize");
            public static readonly int _BlitPaddingSize = Shader.PropertyToID("_BlitPaddingSize");
            public static readonly int _BlitDecodeInstructions = Shader.PropertyToID("_BlitDecodeInstructions");
            public static readonly int _InputDepth = Shader.PropertyToID("_InputDepthTexture");
        }

        /// <summary>
        /// 初始化 Blitter 资源。任何使用前必须先调用一次。
        /// </summary>
        /// <param name="blitPS">Blit 着色器。</param>
        /// <param name="blitColorAndDepthPS">颜色与深度 Blit 着色器。</param>
        public static void Initialize(Shader blitPS, Shader blitColorAndDepthPS)
        {
            if (blit != null)
            {
                throw new Exception("Blitter is already initialized. Please only initialize the blitter once or you will leak engine resources. If you need to re-initialize the blitter with different shaders destroy & recreate it.");
            }

            // 注意：此处创建的任何资源都必须在 Cleanup() 中销毁，
            // 否则会在进入/离开播放模式的循环中泄漏。
            blit = CoreUtils.CreateEngineMaterial(blitPS);
            blitColorAndDepth = CoreUtils.CreateEngineMaterial(blitColorAndDepthPS);

            decodeHdrKeyword = new LocalKeyword(blitPS, "BLIT_DECODE_HDR");

            // 启用纹理数组时，其他系统（如图集）仍需要普通 Blit 版本。
            if (TextureXR.useTexArray)
            {
                blit.EnableKeyword("DISABLE_TEXTURE2D_X_ARRAY");
                blitTexArray = CoreUtils.CreateEngineMaterial(blitPS);
            }

            if (SystemInfo.graphicsShaderLevel < 30)
            {
                /*UNITY_NEAR_CLIP_VALUE*/
                float nearClipZ = -1;
                if (SystemInfo.usesReversedZBuffer)
                    nearClipZ = 1;

                if (!triangleMesh)
                {
                    triangleMesh = new Mesh();
                    triangleMesh.vertices = GetFullScreenTriangleVertexPosition(nearClipZ);
                    triangleMesh.uv = GetFullScreenTriangleTexCoord();
                    triangleMesh.triangles = new int[3] { 0, 1, 2 };
                }

                if (!quadMesh)
                {
                    quadMesh = new Mesh();
                    quadMesh.vertices = GetQuadVertexPosition(nearClipZ);
                    quadMesh.uv = GetQuadTexCoord();
                    quadMesh.triangles = new int[6] { 0, 1, 2, 0, 2, 3 };
                }

                // 应与 Common.hlsl 一致。
                static Vector3[] GetFullScreenTriangleVertexPosition(float z /*= UNITY_NEAR_CLIP_VALUE*/)
                {
                    var r = new Vector3[3];
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 uv = new Vector2((i << 1) & 2, i & 2);
                        r[i] = new Vector3(uv.x * 2.0f - 1.0f, uv.y * 2.0f - 1.0f, z);
                    }
                    return r;
                }

                // 应与 Common.hlsl 一致。
                static Vector2[] GetFullScreenTriangleTexCoord()
                {
                    var r = new Vector2[3];
                    for (int i = 0; i < 3; i++)
                    {
                        if (SystemInfo.graphicsUVStartsAtTop)
                            r[i] = new Vector2((i << 1) & 2, 1.0f - (i & 2));
                        else
                            r[i] = new Vector2((i << 1) & 2, i & 2);
                    }
                    return r;
                }

                // 应与 Common.hlsl 一致。
                static Vector3[] GetQuadVertexPosition(float z /*= UNITY_NEAR_CLIP_VALUE*/)
                {
                    var r = new Vector3[4];
                    for (uint i = 0; i < 4; i++)
                    {
                        uint topBit = i >> 1;
                        uint botBit = (i & 1);
                        float x = topBit;
                        float y = 1 - (topBit + botBit) & 1; // 索引 0、3 时为 1，1、2 时为 0
                        r[i] = new Vector3(x, y, z);
                    }
                    return r;
                }

                // 应与 Common.hlsl 一致。
                static Vector2[] GetQuadTexCoord()
                {
                    var r = new Vector2[4];
                    for (uint i = 0; i < 4; i++)
                    {
                        uint topBit = i >> 1;
                        uint botBit = (i & 1);
                        float u = topBit;
                        float v = (topBit + botBit) & 1; // 索引 0、3 时为 0，1、2 时为 1
                        if (SystemInfo.graphicsUVStartsAtTop)
                            v = 1.0f - v;

                        r[i] = new Vector2(u, v);
                    }
                    return r;
                }
            }
        }

        /// <summary>
        /// 释放 Blitter 资源。
        /// </summary>
        public static void Cleanup()
        {
            CoreUtils.Destroy(blit);
            blit = null;
            CoreUtils.Destroy(blitColorAndDepth);
            blitColorAndDepth = null;
            CoreUtils.Destroy(blitTexArray);
            blitTexArray = null;
            CoreUtils.Destroy(triangleMesh);
            triangleMesh = null;
            CoreUtils.Destroy(quadMesh);
            quadMesh = null;
        }

        /// <summary>
        /// 返回默认的 Blit 材质。
        /// </summary>
        /// <param name="dimension">要 Blit 的纹理维度，2D 或 2D Array。</param>
        /// <returns>对应维度的 Blit 材质。</returns>
        static public Material GetBlitMaterial(TextureDimension dimension)
        {
            bool useTexArray = dimension == TextureDimension.Tex2DArray;
            return useTexArray ? blitTexArray : blit;
        }

        static private void DrawTriangle(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Material material, int shaderPass)
        {
            if (SystemInfo.graphicsShaderLevel < 30)
                cmd.DrawMesh(triangleMesh, Matrix4x4.identity, material, 0, shaderPass, propertyBlock);
            else
                cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 3, 1, propertyBlock);
            propertyBlock.Clear();
        }

        static internal void DrawQuad(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Material material, int shaderPass)
        {
            if (SystemInfo.graphicsShaderLevel < 30)
                cmd.DrawMesh(quadMesh, Matrix4x4.identity, material, 0, shaderPass, propertyBlock);
            else
                cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Quads, 4, 1, propertyBlock);
            propertyBlock.Clear();
        }

        /// <summary>
        /// Blit 一个 RTHandle 纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear)
        {
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevel);
            BlitTexture(cmd, propertyBlock, source, scaleBias, GetBlitMaterial(TextureXR.dimension), bilinear ? 1 : 0);
        }

        /// <summary>
        /// Blit 一个 2D RTHandle 纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitTexture2D(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear)
        {
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevel);
            BlitTexture(cmd, propertyBlock, source, scaleBias, GetBlitMaterial(TextureDimension.Tex2D), bilinear ? 1 : 0);
        }

        /// <summary>
        /// Blit 2D 纹理与深度缓冲。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="sourceColor">颜色源纹理。</param>
        /// <param name="sourceDepth">深度源渲染纹理。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="blitDepth">启用深度 Blit。</param>
        public static void BlitColorAndDepth(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture sourceColor, RenderTexture sourceDepth, Vector4 scaleBias, float mipLevel, bool blitDepth)
        {
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevel);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, sourceColor);
            if (blitDepth)
                propertyBlock.SetTexture(BlitShaderIDs._InputDepth, sourceDepth, RenderTextureSubElement.Depth);
            DrawTriangle(cmd, propertyBlock, blitColorAndDepth, blitDepth ? 1 : 0);
        }

        /// <summary>
        /// Blit 一个 RTHandle 纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移。</param>
        /// <param name="material">Blit 时要调用的材质。</param>
        /// <param name="pass">材质内要调用的 pass 索引。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, Vector4 scaleBias, Material material, int pass)
        {
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            DrawTriangle(cmd, propertyBlock, material, pass);
        }

        /// <summary>
        /// Blit 一个 RTHandle 纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源渲染目标。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移。</param>
        /// <param name="material">Blit 时要调用的材质。</param>
        /// <param name="pass">材质内要调用的 pass 索引。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RenderTargetIdentifier source, Vector4 scaleBias, Material material, int pass)
        {
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            // 遗憾的是无法通过 property block 绑定 RenderTargetIdentifier，只能全局绑定。
            cmd.SetGlobalTexture(BlitShaderIDs._BlitTexture, source);
            DrawTriangle(cmd, propertyBlock, material, pass);
        }

        /// <summary>
        /// 使用指定材质 Blit 纹理。Unity 使用引用名 "_BlitTexture" 绑定输入纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源渲染目标。</param>
        /// <param name="destination">目标渲染目标。</param>
        /// <param name="material">Blit 时要调用的材质。</param>
        /// <param name="pass">材质内要调用的 pass 索引。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, int pass)
        {
            // 遗憾的是无法通过 property block 绑定 RenderTargetIdentifier，只能全局绑定。
            cmd.SetGlobalTexture(BlitShaderIDs._BlitTexture, source);
            cmd.SetRenderTarget(destination);
            DrawTriangle(cmd, propertyBlock, material, pass);
        }

        /// <summary>
        /// 使用指定材质 Blit 纹理。Unity 使用引用名 "_BlitTexture" 绑定输入纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源渲染目标。</param>
        /// <param name="destination">目标渲染目标。</param>
        /// <param name="loadAction">加载操作。</param>
        /// <param name="storeAction">存储操作。</param>
        /// <param name="material">Blit 时要调用的材质。</param>
        /// <param name="pass">材质内要调用的 pass 索引。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, Material material, int pass)
        {
            // 遗憾的是无法通过 property block 绑定 RenderTargetIdentifier，只能全局绑定。
            cmd.SetGlobalTexture(BlitShaderIDs._BlitTexture, source);
            cmd.SetRenderTarget(destination, loadAction, storeAction);
            DrawTriangle(cmd, propertyBlock, material, pass);
        }

        /// <summary>
        /// 使用给定材质 Blit 纹理。Unity 使用引用名 "_BlitTexture" 绑定输入纹理。
        /// 使用前需先设置目标渲染目标。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="scaleBias">采样输入纹理的缩放与偏移值。</param>
        /// <param name="material">Blit 时要调用的材质。</param>
        /// <param name="pass">材质内要调用的 pass 索引。</param>
        public static void BlitTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Vector4 scaleBias, Material material, int pass)
        {
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            DrawTriangle(cmd, propertyBlock, material, pass);
        }

        /// <summary>
        /// 将一个 RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitCameraTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, float mipLevel = 0.0f, bool bilinear = false)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            // 也会设置正确的相机视口。
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, propertyBlock, source, viewportScale, mipLevel, bilinear);
        }

        /// <summary>
        /// 将一个 RThandle Texture2D RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitCameraTexture2D(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, float mipLevel = 0.0f, bool bilinear = false)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            // 也会设置正确的相机视口。
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture2D(cmd, propertyBlock, source, viewportScale, mipLevel, bilinear);
        }

        /// <summary>
        /// 将一个 RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// 此重载允许用户覆盖默认的 Blit 着色器。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="material">Blit 时使用的材质。</param>
        /// <param name="pass">所提供材质要使用的 pass。</param>
        public static void BlitCameraTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, Material material, int pass)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            // 也会设置正确的相机视口。
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, propertyBlock, source, viewportScale, material, pass);
        }

        /// <summary>
        /// 将一个 RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// 此重载允许用户覆盖默认的 Blit 着色器。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="loadAction">加载操作。</param>
        /// <param name="storeAction">存储操作。</param>
        /// <param name="material">Blit 时使用的材质。</param>
        /// <param name="pass">所提供材质要使用的 pass。</param>
        public static void BlitCameraTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, Material material, int pass)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            // 也会设置正确的相机视口。
            CoreUtils.SetRenderTarget(cmd, destination, loadAction, storeAction, ClearFlag.None, Color.clear);
            BlitTexture(cmd, propertyBlock, source, viewportScale, material, pass);
        }

        /// <summary>
        /// 将一个 RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// 此重载允许用户覆盖采样输入 RTHandle 时使用的缩放与偏移。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="scaleBias">采样输入 RTHandle 时使用的缩放与偏移。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitCameraTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, Vector4 scaleBias, float mipLevel = 0.0f, bool bilinear = false)
        {
            // 也会设置正确的相机视口。
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, propertyBlock, source, scaleBias, mipLevel, bilinear);
        }

        /// <summary>
        /// 将一个 RTHandle Blit 到另一个 RTHandle。
        /// 此方法会正确处理纹理在当前视口下被部分使用（按分辨率）的情况。
        /// 此重载允许用户覆盖目标 RTHandle 的视口。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 RTHandle。</param>
        /// <param name="destination">目标 RTHandle。</param>
        /// <param name="destViewport">目标 RTHandle 的视口。</param>
        /// <param name="mipLevel">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitCameraTexture(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, RTHandle destination, Rect destViewport, float mipLevel = 0.0f, bool bilinear = false)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            CoreUtils.SetRenderTarget(cmd, destination);
            cmd.SetViewport(destViewport);
            BlitTexture(cmd, propertyBlock, source, viewportScale, mipLevel, bilinear);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形 Blit 一个纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="scaleBiasTex">输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        public static void BlitQuad(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);

            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), bilinear ? 3 : 2);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形 Blit 一个纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="textureSize">源纹理尺寸。</param>
        /// <param name="scaleBiasTex">采样输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        /// <param name="paddingInPixels">以像素为单位的边距。</param>
        public static void BlitQuadWithPadding(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector2 textureSize, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear, int paddingInPixels)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitTextureSize, textureSize);
            propertyBlock.SetInt(BlitShaderIDs._BlitPaddingSize, paddingInPixels);
            if (source.wrapMode == TextureWrapMode.Repeat)
                DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), bilinear ? 7 : 6);
            else
                DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), bilinear ? 5 : 4);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形 Blit 一个纹理，并与渲染目标上的现有内容做 alpha 混合。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="textureSize">源纹理尺寸。</param>
        /// <param name="scaleBiasTex">采样输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        /// <param name="paddingInPixels">以像素为单位的边距。</param>
        public static void BlitQuadWithPaddingMultiply(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector2 textureSize, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear, int paddingInPixels)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitTextureSize, textureSize);
            propertyBlock.SetInt(BlitShaderIDs._BlitPaddingSize, paddingInPixels);
            if (source.wrapMode == TextureWrapMode.Repeat)
                DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), bilinear ? 12 : 11);
            else
                DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), bilinear ? 10 : 9);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形 Blit 一个八面体投影纹理。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="textureSize">源纹理尺寸。</param>
        /// <param name="scaleBiasTex">采样输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        /// <param name="paddingInPixels">以像素为单位的边距。</param>
        public static void BlitOctahedralWithPadding(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector2 textureSize, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear, int paddingInPixels)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitTextureSize, textureSize);
            propertyBlock.SetInt(BlitShaderIDs._BlitPaddingSize, paddingInPixels);
            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), 8);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形 Blit 一个八面体投影纹理，
        /// 并与渲染目标上的现有内容做 alpha 混合。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="textureSize">源纹理尺寸。</param>
        /// <param name="scaleBiasTex">采样输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        /// <param name="paddingInPixels">以像素为单位的边距。</param>
        public static void BlitOctahedralWithPaddingMultiply(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector2 textureSize, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear, int paddingInPixels)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitTextureSize, textureSize);
            propertyBlock.SetInt(BlitShaderIDs._BlitPaddingSize, paddingInPixels);
            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), 13);
        }

        /// <summary>
        /// 将 cube 纹理作为八面体四边形 Blit 到 2d 纹理中（投影）。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 cube 纹理。</param>
        /// <param name="mipLevelTex">要采样的 mip 级别。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        public static void BlitCubeToOctahedral2DQuad(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector4 scaleBiasRT, int mipLevelTex)
        {
            propertyBlock.SetTexture(BlitShaderIDs._BlitCubeTexture, source);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, new Vector4(1, 1, 0, 0));
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), 14);
        }

        /// <summary>
        /// 将 cube 纹理作为带边距的八面体四边形 Blit 到 2d 纹理中（投影）。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源 cube 纹理。</param>
        /// <param name="textureSize">源纹理尺寸。</param>
        /// <param name="mipLevelTex">要采样的 mip 级别。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="bilinear">启用双线性过滤。</param>
        /// <param name="paddingInPixels">以像素为单位的边距。</param>
        /// <param name="decodeInstructions">解码指令。</param>
        public static void BlitCubeToOctahedral2DQuadWithPadding(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector2 textureSize, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear, int paddingInPixels, Vector4? decodeInstructions = null)
        {
            var material = GetBlitMaterial(source.dimension);

            propertyBlock.SetTexture(BlitShaderIDs._BlitCubeTexture, source);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, new Vector4(1, 1, 0, 0));
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetVector(BlitShaderIDs._BlitTextureSize, textureSize);
            propertyBlock.SetInt(BlitShaderIDs._BlitPaddingSize, paddingInPixels);

            cmd.SetKeyword(material, decodeHdrKeyword, decodeInstructions.HasValue);
            if (decodeInstructions.HasValue)
            {
                propertyBlock.SetVector(BlitShaderIDs._BlitDecodeInstructions, decodeInstructions.Value);
            }

            DrawQuad(cmd, propertyBlock, material, bilinear ? 22 : 21);
            cmd.SetKeyword(material, decodeHdrKeyword, false);
        }

        /// <summary>
        /// 将 cube 纹理作为八面体四边形 Blit 到 2d 纹理中（投影）。
        /// 支持单通道与多通道格式之间的转换：
        /// RGB(A) 到 YYYY（亮度）；R 到 RRRR；A 到 AAAA。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        public static void BlitCubeToOctahedral2DQuadSingleChannel(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector4 scaleBiasRT, int mipLevelTex)
        {
            int pass = 15;
            uint sourceChnCount = GraphicsFormatUtility.GetComponentCount(source.graphicsFormat);
            if (sourceChnCount == 1)
            {
                if (GraphicsFormatUtility.IsAlphaOnlyFormat(source.graphicsFormat))
                    pass = 16;
                if (GraphicsFormatUtility.GetSwizzleR(source.graphicsFormat) == FormatSwizzle.FormatSwizzleR)
                    pass = 17;
            }

            propertyBlock.SetTexture(BlitShaderIDs._BlitCubeTexture, source);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, new Vector4(1, 1, 0, 0));
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), pass);
        }

        /// <summary>
        /// 在当前渲染目标上用四边形双线性 Blit 一个纹理。
        /// 支持单通道与多通道格式之间的转换：
        /// RGB(A) 到 YYYY（亮度）；R 到 RRRR；A 到 AAAA。
        /// </summary>
        /// <param name="cmd">用于渲染的命令缓冲。</param>
        /// <param name="source">源纹理。</param>
        /// <param name="scaleBiasTex">采样输入纹理的缩放与偏移。</param>
        /// <param name="scaleBiasRT">输出纹理的缩放与偏移。</param>
        /// <param name="mipLevelTex">要 Blit 的 mip 级别。</param>
        public static void BlitQuadSingleChannel(CommandBuffer cmd, MaterialPropertyBlock propertyBlock, Texture source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex)
        {
            int pass = 18;
            uint sourceChnCount = GraphicsFormatUtility.GetComponentCount(source.graphicsFormat);
            if (sourceChnCount == 1)
            {
                if (GraphicsFormatUtility.IsAlphaOnlyFormat(source.graphicsFormat))
                    pass = 19;
                if (GraphicsFormatUtility.GetSwizzleR(source.graphicsFormat) == FormatSwizzle.FormatSwizzleR)
                    pass = 20;
            }

            propertyBlock.SetTexture(BlitShaderIDs._BlitTexture, source);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBiasTex);
            propertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            propertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevelTex);

            DrawQuad(cmd, propertyBlock, GetBlitMaterial(source.dimension), pass);
        }
    }
}
