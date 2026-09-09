#ifndef HNRP_LIT_LIGHTING_INCLUDED
#define HNRP_LIT_LIGHTING_INCLUDED

#include "../Light/Light.hlsl"
#include "../Lighting/Lighting.hlsl"
#include "../Lighting/BRDF.hlsl"

struct LightingData
{
    DirectLightingData mainDirectLight;
    DirectLightingData additionalDirectLight;
    IndirectLightingData indirectLight;
};

struct LightingOutputData
{
    float3 lightingColor;
    float alpha;
};

void BuildPreBRDFData(LitVaryings litVaryings, out PreBRDFData preBRDFData)
{
    InitializePreBRDFData(litVaryings.normalWS, litVaryings.tangentWS, litVaryings.positionWS, preBRDFData);
}

void BuildBRDFData(LitSurfaceData litSurfaceData, PreBRDFData preBRDFData, out BRDFData brdfData)
{
    InitializeBRDFData(litSurfaceData.albedo, litSurfaceData.normalTS, litSurfaceData.smoothness, litSurfaceData.metallic, preBRDFData, brdfData);
}

void BuildLightingInputData(LitVaryings litVaryings, BRDFData brdfData, out LightingInputData lightingInputData)
{
    InitializeLightingInputData(litVaryings.positionWS, litVaryings.uv0, litVaryings.staticLightmapUV, litVaryings.vertexSH, brdfData.normalWS, litVaryings.positionCS, lightingInputData);
}

void BuildBRDFLightingData(PreBRDFData preBRDFData, BRDFData brdfData, LightingInputData lightingInputData, out BRDFLightingData brdfLightingData)
{
    InitializeBRDFLightingData(brdfData.normalWS, preBRDFData.viewDirectionWS, lightingInputData.mainLight, brdfLightingData);
}

void BuildLightingData(LitSurfaceData litSurfaceData, BRDFData brdfData, LightingInputData lightingInputData, BRDFLightingData brdfLightingData, out LightingData lightingData)
{
    ZERO_INITIALIZE(LightingData, lightingData);

    uint meshRenderingLayers = GetMeshRenderingLayer();
    if(IsMatchingLightLayer(lightingInputData.mainLight.renderingLayerMask, meshRenderingLayers))
    {
        float3 mainLightRadiance = DirectLightingDiffuseRadiance(lightingInputData.mainLight, brdfLightingData.saturateNdotL);
        float mainLightSpecularTerm = DirectBRDFSpecular(brdfData, brdfLightingData);
        lightingData.mainDirectLight = DirectLightingPBR(brdfData.diffuse, mainLightRadiance, brdfData.specular, mainLightSpecularTerm);
#if _ALPHAPREMULTIPLY_ON
        lightingData.mainDirectLight.diffuse *= litSurfaceData.alpha;
#endif
    }

    uint lightCount = GetAdditionalLightsCount();
    float2 normalizedScreenSpaceUV = lightingInputData.normalizedScreenSpaceUV;
    float3 positionWS = lightingInputData.positionWS;
    LIGHT_LOOP_BEGIN(lightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);
        if(IsMatchingLightLayer(light.renderingLayerMask, meshRenderingLayers))
        {
            float saturateNdotL = saturate(dot(brdfData.normalWS, light.directionWS));
            float3 diffuseRadiance = DirectLightingDiffuseRadiance(light, saturateNdotL);
            float lightSpecularTerm = DirectBRDFSpecular(brdfData, brdfLightingData);
            float3 specularRadiance = DirectLightingSpecularRadiance(light, lightSpecularTerm);
            DirectLightingData additionalDirectLight = DirectLightingPBR(brdfData.diffuse, diffuseRadiance, brdfData.specular, specularRadiance);
            lightingData.additionalDirectLight.diffuse += additionalDirectLight.diffuse;
            lightingData.additionalDirectLight.specular += additionalDirectLight.specular;
        }
    LIGHT_LOOP_END

    float3 envSpecular = EnvironmentBRDFSpecular(brdfData, brdfLightingData);
    float3 envReflection = GlossyEnvironmentReflection(brdfData.refViewDirectionWS, positionWS, brdfData.perceptualRoughness, 1.0, normalizedScreenSpaceUV);
    lightingData.indirectLight = IndirectLightingPBR(brdfData.diffuse, lightingInputData.bakedGI, envSpecular, envReflection);
}

void BuildLightingOutputData(LitSurfaceData litSurfaceData, LightingData lightingData, out LightingOutputData lightingOutputData)
{
    ZERO_INITIALIZE(LightingOutputData, lightingOutputData);

    lightingOutputData.lightingColor.rgb = 
        lightingData.mainDirectLight.diffuse.rgb 
        + lightingData.mainDirectLight.specular.rgb
        + lightingData.additionalDirectLight.diffuse.rgb
        + lightingData.additionalDirectLight.specular.rgb
        + lightingData.indirectLight.diffuse.rgb
        + lightingData.indirectLight.specular.rgb
        ;
    lightingOutputData.lightingColor.rgb += litSurfaceData.emission.rgb;
    lightingOutputData.alpha = litSurfaceData.alpha;
}

#endif