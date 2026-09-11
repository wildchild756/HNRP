#ifndef HNRP_LIGHTING_INCLUDED
#define HNRP_LIGHTING_INCLUDED

struct LightingInputData
{
    float3 positionWS;
    Light mainLight;
    float2 mainUV;
    float3 bakedGI;
    float2 normalizedScreenSpaceUV;
};

struct DirectLightingData
{
    float3 diffuse;
    float3 specular;

    DirectLightingData Add(DirectLightingData other)
    {
        DirectLightingData result;
        result.diffuse = diffuse + other.diffuse;
        result.specular = specular + other.specular;
        return result;
    }
};

struct IndirectLightingData
{
    float3 diffuse;
    float3 specular;

    IndirectLightingData Add(IndirectLightingData other)
    {
        IndirectLightingData result;
        result.diffuse = diffuse + other.diffuse;
        result.specular = specular + other.specular;
        return result;
    }
};

void InitializeLightingInputData(float3 positionWS, float2 mainUV, float2 staticLightmapUV, float3 vertexSH, float3 normalWS, float4 positionCS, out LightingInputData lightingInputData)
{
    ZERO_INITIALIZE(LightingInputData, lightingInputData);

    lightingInputData.positionWS = positionWS;

    Light mainLight = GetMainLight(positionWS);
    lightingInputData.mainLight = mainLight;

    lightingInputData.mainUV = mainUV;

    float3 bakedGI = SAMPLE_GI(staticLightmapUV, vertexSH, normalWS);
    lightingInputData.bakedGI = bakedGI;

    float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    lightingInputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;
}

float3 DirectLightingDiffuseRadiance(Light light, float NdotL)
{
    return light.color * (light.shadowAttenuation * light.distanceAttenuation * NdotL);
}

float3 DirectLightingSpecularRadiance(Light light, float specularTerm)
{
    return light.color * (light.shadowAttenuation * light.distanceAttenuation * specularTerm);
}

DirectLightingData DirectLightingPBR(float3 diffuse, float3 diffuseRadiance, float3 specular, float3 specularRadiance)
{
    DirectLightingData directLightingData;
    ZERO_INITIALIZE(DirectLightingData, directLightingData);

    directLightingData.diffuse = diffuse * diffuseRadiance;
    directLightingData.specular = specular * specularRadiance;

    return directLightingData;
}

IndirectLightingData IndirectLightingPBR(float3 diffuse, float3 bakedGI, float3 envSpecular, float3 envReflection)
{
    IndirectLightingData indirectLightingData;
    ZERO_INITIALIZE(IndirectLightingData, indirectLightingData);

    indirectLightingData.diffuse = bakedGI * diffuse;
    indirectLightingData.specular = envSpecular * envReflection;

    return indirectLightingData;
}

#endif