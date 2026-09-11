#ifndef HNRP_LIGHT_INCLUDED
#define HNRP_LIGHT_INCLUDED

#include "../ClusterCulling/ClusterCullingLight.hlsl"
#include "../Shadow/Shadow.hlsl"

struct Light
{
    float3 color;
    float3 positionWS;
    float3 directionWS;
    float shadowAttenuation;
    float distanceAttenuation;
    uint renderingLayerMask;
};

#if CLUSTER_CULLING_LIGHT
    #define LIGHT_LOOP_BEGIN(lightCount) { \
    uint lightIndex; \
    ClusterCullingLightIterator _internal_clusterIterator = ClusterCullingLightInit(normalizedScreenSpaceUV, positionWS); \
    [loop] while (ClusterCullingLightNext(_internal_clusterIterator, lightIndex)) { \
        if(lightIndex > MAX_DIRECTIONAL_LIGHT_ON_SCREEN + MAX_LOCAL_LIGHT_ON_SCREEN) break; \
        if(lightIndex == _CLUSTER_CULLING_LIGHT_MAIN_LIGHT_INDEX) continue;
        
    #define LIGHT_LOOP_END } }
#else
    #define LIGHT_LOOP_BEGIN(lightCount) \
    for(uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex) { \
        if(lightIndex == _LightConstantData.x) continue;

    #define LIGHT_LOOP_END }
#endif

float3 GetViewDirectionWS(float3 positionWS)
{
    return normalize(GetCameraPositionWS().xyz - positionWS);
}

Light GetMainLight(float3 positionWS)
{
    int mainLightIndex = 0;
#if CLUSTER_CULLING_LIGHT
    mainLightIndex = _CLUSTER_CULLING_LIGHT_MAIN_LIGHT_INDEX;
#else
    mainLightIndex = _LightConstantData.x;
#endif
    Light light;
    ZERO_INITIALIZE(Light, light);
    light.color = _LightDatasBuffer[mainLightIndex].color;
    light.directionWS = _LightDatasBuffer[mainLightIndex].directionWS;
    light.shadowAttenuation = GetShadowAttenuation((uint)mainLightIndex, positionWS, light.directionWS);
    light.distanceAttenuation = 1.0;
    light.renderingLayerMask = asuint(_LightDatasBuffer[mainLightIndex].renderingLayerMask);

    return light;
}

Light GetAdditionalLight(uint lightIndex, float3 positionWS)
{
    Light light;
    light.color = _LightDatasBuffer[lightIndex].color;
    light.directionWS = _LightDatasBuffer[lightIndex].directionWS;
    light.positionWS = _LightDatasBuffer[lightIndex].positionWS;
    light.shadowAttenuation = 1.0;
    uint lightType = asuint(_LightDatasBuffer[lightIndex].lightType);
    if(lightType == 1/* Directional light */)
    {
        light.distanceAttenuation = 1.0;
    }
    else if(lightType == 2 /* Point Light */|| lightType == 3 /* Spot Light */)
    {
        float3 lightVector = light.positionWS - positionWS;
        float distanceSqr = max(dot(lightVector, lightVector), FLT_MIN);
        light.directionWS = lightVector * rsqrt(distanceSqr);
        float distanceAttenuation = DistanceAttenuation(lightVector, _LightDatasBuffer[lightIndex].range);
        light.distanceAttenuation = distanceAttenuation;
        if(lightType == 3 /* Spot Light */)
        {
            float angleAttenuation = AngleAttenuation(_LightDatasBuffer[lightIndex].directionWS, light.directionWS, _LightDatasBuffer[lightIndex].attenuation.zw);
            light.distanceAttenuation = distanceAttenuation * angleAttenuation;
        }
    }
    light.renderingLayerMask = asuint(_LightDatasBuffer[lightIndex].renderingLayerMask);
    light.shadowAttenuation = GetShadowAttenuation(lightIndex, positionWS, light.directionWS);

    return light;
}

uint GetAdditionalLightsCount()
{
#if CLUSTER_CULLING_LIGHT
    return _CLUSTER_CULLING_LIGHT_LOCAL_LIGHT_COUNT + _CLUSTER_CULLING_LIGHT_DIRECTIONAL_LIGHT_COUNT;
#else
    return asuint(_LightConstantData.y);
#endif
}

#endif