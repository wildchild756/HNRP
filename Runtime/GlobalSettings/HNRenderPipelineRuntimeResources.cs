using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    public class HNRenderPipelineRuntimeResources : RenderPipelineResources
    {
        void OnEnable()
        {
            emptyTexture = Texture2D.blackTexture;
            emptyBuffer = new ComputeBuffer(1, 4);
        }


        protected override string packagePath => HNRenderPipelineGlobalSettings.HNRenderPipelinePath;

        public ShaderResources shaderResources;

        public Texture emptyTexture;
        public ComputeBuffer emptyBuffer;

        /// <summary>ComputeShader for cluster-based light culling.</summary>
        public ComputeShader clusterCullingLightCS;

        /// <summary>ComputeShader for cluster-based reflection probe culling.</summary>
        public ComputeShader clusterCullingReflectionProbeCS;

        /// <summary>按名称获取外部导入纹理（供资源节点导入）。</summary>
        public Texture GetExternalTexture(string name)
        {
            if (name == "emptyTexture") return emptyTexture;
            return null;
        }


        [Serializable, ReloadGroup]
        public class ShaderResources
        {
            [Reload("Runtime/ShaderLibrary/Shaders/Lit.shader")]
            public Shader Lit;

            [Reload("Runtime/ShaderLibrary/Shaders/Blit.shader")]
            public Shader Blit;

            [Reload("Runtime/ShaderLibrary/Shaders/BlitColorAndDepth.shader")]
            public Shader BlitColorAndDepth;

            [Reload("Runtime/ShaderLibrary/Shaders/ShadowClear.shader")]
            public Shader ShadowClear;
        }
    }
}
