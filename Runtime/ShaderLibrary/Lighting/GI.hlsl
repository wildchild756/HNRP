#ifndef HNRP_GI_INCLUDED
#define HNRP_GI_INCLUDED

#include "../ClusterCulling/ClusterCullingReflectionProbe.hlsl"

#if !defined(_MIXED_LIGHTING_SUBTRACTIVE) && defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK)
    #define _MIXED_LIGHTING_SUBTRACTIVE
#endif

float3 SampleSH(float3 normalWS)
{
    float4 SHCoefficients[7];
    SHCoefficients[0] = unity_SHAr;
    SHCoefficients[1] = unity_SHAg;
    SHCoefficients[2] = unity_SHAb;
    SHCoefficients[3] = unity_SHBr;
    SHCoefficients[4] = unity_SHBg;
    SHCoefficients[5] = unity_SHBb;
    SHCoefficients[6] = unity_SHC;

    return max(float3(0.0, 0.0, 0.0), SampleSH9(SHCoefficients, normalWS));
}

float3 SampleSHVertex(float3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return SampleSH(normalWS);
#elif defined(EVALUATE_SH_MIXED)
    return SHEvalLinearL2(normalWS, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
#endif
    return float3(0.0, 0.0, 0.0);
}

float3 SampleSHPixel(float3 L2Term, float3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return L2Term;
#elif defined(EVALUATE_SH_MIXED)
    half3 res = L2Term + SHEvalLinearL0L1(normalWS, unity_SHAr, unity_SHAg, unity_SHAb);
#ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
#endif
    return max(half3(0, 0, 0), res);
#endif

    return SampleSH(normalWS);
}

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
#define LIGHTMAP_NAME unity_Lightmaps
#define LIGHTMAP_INDIRECTION_NAME unity_LightmapsInd
#define LIGHTMAP_SAMPLER_NAME samplerunity_Lightmaps
#define LIGHTMAP_SAMPLE_EXTRA_ARGS staticLightmapUV, unity_LightmapIndex.x
#else
#define LIGHTMAP_NAME unity_Lightmap
#define LIGHTMAP_INDIRECTION_NAME unity_LightmapInd
#define LIGHTMAP_SAMPLER_NAME samplerunity_Lightmap
#define LIGHTMAP_SAMPLE_EXTRA_ARGS staticLightmapUV
#endif

float3 SampleLightmap(float2 staticLightmapUV, float3 normalWS)
{
#if defined(UNITY_LIGHTMAP_FULL_HDR)
    bool encodedLightmap = false;
#else
    bool encodedLightmap = true;
#endif

    float4 decodeInstructions = float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0);
    float4 transformCoords = float4(1.0, 1.0, 0.0, 0.0);
    
    float3 diffuseLighting = 0;

#if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
    diffuseLighting = SampleDirectionalLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME),
        TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_INDIRECTION_NAME, LIGHTMAP_SAMPLER_NAME),
        LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, normalWS, encodedLightmap, decodeInstructions);
#elif defined(LIGHTMAP_ON)
    diffuseLighting = SampleSingleLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME), LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, encodedLightmap, decodeInstructions);
#endif

    return diffuseLighting;
}

#if defined(LIGHTMAP_ON)
#define SAMPLE_GI(staticLmName, shName, normalWSName) SampleLightmap(staticLmName, normalWSName)
#else
#define SAMPLE_GI(staticLmName, shName, normalWSName) SampleSHPixel(shName, normalWSName)
#endif

half3 BoxProjectedCubemapDirection(half3 reflectionWS, float3 positionWS, float3 cubemapPositionWS, float3 boxMin, float3 boxMax)
{
    float3 boxMinMax = (reflectionWS > 0.0f) ? boxMax.xyz : boxMin.xyz;
    half3 rbMinMax = half3(boxMinMax - positionWS) / reflectionWS;

    half fa = half(min(min(rbMinMax.x, rbMinMax.y), rbMinMax.z));

    half3 worldPos = half3(positionWS - cubemapPositionWS.xyz);

    half3 result = worldPos + reflectionWS * fa;
    return result;
}

float CalculateProbeWeight(float3 positionWS, float4 probeBoxMin, float4 probeBoxMax)
{
    float blendDistance = probeBoxMax.w;
    float3 weightDir = min(positionWS - probeBoxMin.xyz, probeBoxMax.xyz - positionWS) / blendDistance;
    return saturate(min(weightDir.x, min(weightDir.y, weightDir.z)));
}

float CalculateProbeBoxWeight(float3 positionWS, float3 probeBoxMin, float3 probeBoxMax, float blendDistance)
{
    // 盒外向外的衰减权重：盒内（含盒面）为 1，盒外 blendDistance 处衰减到 0。
    // 影响范围 = box + blendDistance，与探针剔除所用的外扩包围盒一致。
    float3 outside = max(probeBoxMin - positionWS, positionWS - probeBoxMax);
    float outsideDistance = max(outside.x, max(outside.y, outside.z));
    return saturate(1.0 - outsideDistance / max(blendDistance, 1e-6));
}

half CalculateProbeVolumeSqrMagnitude(float4 probeBoxMin, float4 probeBoxMax)
{
    half3 maxToMin = half3(probeBoxMax.xyz - probeBoxMin.xyz);
    return dot(maxToMin, maxToMin);
}

float2 GetReflectionProbeAtlasUV(float3 reflectVector, float4 scaleOffset, float mip)
{
    float2 uv = saturate(PackNormalOctQuadEncode(reflectVector) * 0.5 + 0.5);
    float2 padding = (float)REFLECTION_PROBE_ATLAS_TEXEL_PADDING / REFLECTION_PROBE_ATLAS_SIZE;
    padding *= pow(2.0, mip);
    float2 size = scaleOffset.xy - padding;
    float2 offset = scaleOffset.zw + 0.5 * padding;
    return uv * size + offset;
}

half3 CalculateIrradianceFromReflectionProbes(half3 reflectVector, float3 positionWS, half perceptualRoughness, float2 normalizedScreenSpaceUV)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);
#if CLUSTER_CULLING_REFLECTION_PROBE
    float totalWeight = 0.0f;
    uint probeIndex;
    ClusterCullingReflectionProbeIterator it = ClusterCullingReflectionProbeInit(normalizedScreenSpaceUV, positionWS);
    [loop] while (ClusterCullingReflectionProbeNext(it, probeIndex))
    {
        if (probeIndex >= MAX_REFLECTION_PROBES_ON_SCREEN)
            continue;

        float3 probeBoxMax = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].boxMax;
        float3 probeBoxMin = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].boxMin;
        float3 probePositionWS = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].positionWS;
        float4 scaleOffset = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].scaleOffset;
        float blendDistance = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].blendDistance;
        float importance = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].importance;
        float intensity = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].intensity;
        uint mipCount = _ClusterCullingReflectionProbeDatasBuffer[probeIndex].mipCount;

        half probeWeight = half(CalculateProbeBoxWeight(positionWS, probeBoxMin, probeBoxMax, blendDistance));
        if (probeWeight > 0.01h)
        {
            probeWeight *= importance;
            half3 reflectVectorProbe = reflectVector;
            reflectVectorProbe = BoxProjectedCubemapDirection(reflectVector, positionWS, probePositionWS, probeBoxMin, probeBoxMax);
            reflectVectorProbe = normalize(reflectVectorProbe);
            half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness, mipCount - 1);
            float2 uv = GetReflectionProbeAtlasUV(reflectVectorProbe, scaleOffset, mip);
            float3 irradianceColor = SAMPLE_TEXTURE2D_LOD(_ReflectionProbeAtlas, sampler_TrilinearClamp, uv, mip).xyz;
            irradiance += irradianceColor * probeWeight * intensity;
            totalWeight += probeWeight;
        }
    }
    // 仅在权重之和超过 1 时才归一化（避免多探针叠加过亮）。
    // 权重 <= 1 时保留权重本身，使单探针能按 blendDistance 平滑衰减；
    // 若像原来那样无条件除以 totalWeight，单探针的权重会被约掉，
    // 表现为只有外扩范围、没有过渡衰减（硬边）。
    irradiance = totalWeight > 0.0f ? irradiance / max(totalWeight, 1.0f) : 0/* TODO:global reflection probe */;
#else
    irradiance = float3(0, 0, 0);
#endif
    return irradiance;
}

half3 GlossyEnvironmentReflection(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion, float2 normalizedScreenSpaceUV)
{
    half3 irradiance;
    irradiance = CalculateIrradianceFromReflectionProbes(reflectVector, positionWS, perceptualRoughness, normalizedScreenSpaceUV);
    return irradiance * occlusion;
}

#endif