#ifndef HNRP_INPUT_INCLUDED
#define HNRP_INPUT_INCLUDED

#include "./UnityInput.hlsl"

#define MAX_DIRECTIONAL_LIGHT_ON_SCREEN (16)
#define MAX_LOCAL_LIGHT_ON_SCREEN (512)

#define MAX_REFLECTION_PROBE_MASK_WORDS (16384)
#define MAX_REFLECTION_PROBES_ON_SCREEN (64)
#define REFLECTION_PROBE_ATLAS_MIP_COUNT (7)
#define REFLECTION_PROBE_ATLAS_TEXEL_PADDING (2)
#define REFLECTION_PROBE_ATLAS_SIZE (4096)

// Light Data Structure
struct LightData
{
    float3 positionWS;
    uint lightType;
    float3 color;
    float range;
    float4 attenuation;
    float3 directionWS;
    uint renderingLayerMask;
};

// Light Data Buffer
StructuredBuffer<LightData> _LightDatasBuffer;

#if CLUSTER_CULLING_REFLECTION_PROBE

struct ClusterCullingReflectionProbeDatas
{
    float3 boxMax;
    float blendDistance;
    float3 boxMin;
    float importance;
    float3 positionWS;
    float intensity;
    float4 scaleOffset;
    float mipCount;
    float3 unused;
};

StructuredBuffer<ClusterCullingReflectionProbeDatas> _ClusterCullingReflectionProbeDatasBuffer;

GLOBAL_CBUFFER_START(_ClusterCullingReflectionProbeParamsBuffer, b2)
    float2 _ClusterCullingReflectionProbeClusterSizeXY;
    float2 _ClusterCullingReflectionProbeClusterZScaleOffset;
    int _ClusterCullingReflectionProbeWordsPerCluster;
    int _ClusterCullingReflectionProbeReflectionProbeCount;
    float _ClusterCullingReflectionProbeUnused0;
    float _ClusterCullingReflectionProbeUnused1;
CBUFFER_END

#define _CLUSTER_CULLING_REFLECTION_PROBE_XY_SCALE (_ClusterCullingReflectionProbeClusterSizeXY.xy)
#define _CLUSTER_CULLING_REFLECTION_PROBE_Z_SCALE (_ClusterCullingReflectionProbeClusterZScaleOffset.x)
#define _CLUSTER_CULLING_REFLECTION_PROBE_Z_OFFSET (_ClusterCullingReflectionProbeClusterZScaleOffset.y)
#define _CLUSTER_CULLING_REFLECTION_PROBE_WORDS_PER_CLUSTER (_ClusterCullingReflectionProbeWordsPerCluster)
#define _CLUSTER_CULLING_REFLECTION_PROBE_COUNT (_ClusterCullingReflectionProbeReflectionProbeCount)

StructuredBuffer<uint> _ClusterCullingReflectionProbeMaskBuffer;

TEXTURE2D(_ReflectionProbeAtlas);

#endif

#if CLUSTER_CULLING_LIGHT

GLOBAL_CBUFFER_START(_ClusterCullingLightParamsBuffer, b3)
    float2 _ClusterCullingLightClusterSize;
    float2 _ClusterCullingLightClusterZScaleOffset;
    int _ClusterCullingLightWordsPerCluster;
    int _ClusterCullingLightDirectionalLightCount;
    int _ClusterCullingLightLocalLightCount;
    int _ClusterCullingLightMainLightIndex;
CBUFFER_END

#define _CLUSTER_CULLING_LIGHT_XY_SCALE (_ClusterCullingLightClusterSize)
#define _CLUSTER_CULLING_LIGHT_Z_SCALE (_ClusterCullingLightClusterZScaleOffset.x)
#define _CLUSTER_CULLING_LIGHT_Z_OFFSET (_ClusterCullingLightClusterZScaleOffset.y)
#define _CLUSTER_CULLING_LIGHT_WORDS_PER_CLUSTER (_ClusterCullingLightWordsPerCluster)
#define _CLUSTER_CULLING_LIGHT_DIRECTIONAL_LIGHT_COUNT (_ClusterCullingLightDirectionalLightCount)
#define _CLUSTER_CULLING_LIGHT_LOCAL_LIGHT_COUNT (_ClusterCullingLightLocalLightCount)
#define _CLUSTER_CULLING_LIGHT_MAIN_LIGHT_INDEX (_ClusterCullingLightMainLightIndex)

StructuredBuffer<uint> _ClusterCullingLightMaskBuffer;

#endif

#endif