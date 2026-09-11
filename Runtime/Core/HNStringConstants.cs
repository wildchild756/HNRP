using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    public static class ShaderPassNames
    {
        public static readonly string ForwardStr = "Forward";
        public static readonly string ShadowCasterStr = "ShadowCaster";


        public static readonly ShaderTagId ForwardName = new ShaderTagId(ForwardStr);
        public static readonly ShaderTagId ShadowCasterName = new ShaderTagId(ShadowCasterStr);


        public static readonly ShaderTagId[] AllForwardNames = new[] { ForwardName };
        public static readonly ShaderTagId[] AllShadowCasterNames = new[] { ShadowCasterName };
    }


    public static class GlobalPropertyIDs
    {
        public static readonly int ShaderVariablesGlobal = Shader.PropertyToID("ShaderVariablesGlobal");
        public static readonly int glossyEnvironmentCubeMap = Shader.PropertyToID("_GlossyEnvironmentCubeMap");
    }


    public static class MaterialPropertys
    {
        public static readonly string surfaceType = "_SurfaceType";
        public static readonly string blendMode = "_BlendMode";
        public static readonly string srcBlend = "_SrcBlend";
        public static readonly string dstBlend = "_DstBlend";
        public static readonly string srcBlendAlpha = "_SrcBlendAlpha";
        public static readonly string dstBlendAlpha = "_DstBlendAlpha";
        public static readonly string alphaClip = "_AlphaClip";
        public static readonly string cutoff = "_Cutoff";
        public static readonly string cullMode = "_CullMode";
        public static readonly string ztestMode = "_ZTestMode";
        public static readonly string zwrite = "_ZWrite";
        public static readonly string queueOffset = "_QueueOffset";

        public static readonly string baseMap = "_BaseMap";
        public static readonly string baseColor = "_BaseColor";
        public static readonly string alphaRemapMin = "_AlphaRemapMin";
        public static readonly string alphaRemapMax = "_AlphaRemapMax";
        public static readonly string maskMap = "_MaskMap";
        public static readonly string metallicRemapMin = "_MetallicRemapMin";
        public static readonly string metallicRemapMax = "_MetallicRemapMax";
        public static readonly string smoothnessRemapMin = "_SmoothnessRemapMin";
        public static readonly string smoothnessRemapMax = "_SmoothnessRemapMax";
        public static readonly string aoRemapMin = "_AORemapMin";
        public static readonly string aoRemapMax = "_AORemapMax";
        public static readonly string metallic = "_Metallic";
        public static readonly string smoothness = "_Smoothness";
        public static readonly string normalMap = "_NormalMap";
        public static readonly string normalScale = "_NormalScale";
        public static readonly string emissionMap = "_EmissionMap";
        public static readonly string emissionColor = "_EmissionColor";
    }


    public static class MaterialLitKeywords
    {
        public static readonly string alphaPremultiply = "_ALPHAPREMULTIPLY_ON";
        public static readonly string alphaTest = "_ALPHATEST_ON";
        public static readonly string basemap = "_BASEMAP";
        public static readonly string normalMap = "_NORMALMAP";
        public static readonly string maskMap = "_MASKMAP";
        public static readonly string emissionMap = "_EMISSIONMAP";
    }


    public static class GlobalKeywords
    {
        public static readonly string evaluateSHMixed = "EVALUATE_SH_MIXED";
        public static readonly string evaluateSHVertex = "EVALUATE_SH_VERTEX";
        public static readonly string shadowMap = "SHADOW_MAP";
        public static readonly string screenSpaceShadowMap = "SCREEN_SPACE_SHADOW_MAP";
        public static readonly string clusterCullingReflectionProbe = "CLUSTER_CULLING_REFLECTION_PROBE";
        public static readonly string clusterCullingLight = "CLUSTER_CULLING_LIGHT";
    }
}
