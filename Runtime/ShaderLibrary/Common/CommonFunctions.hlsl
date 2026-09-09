#ifndef HNRP_COMMON_FUNCTIONS_INCLUDED
#define HNRP_COMMON_FUNCTIONS_INCLUDED

float Remap(float t, float x0, float y0, float x1, float y1)
{
    return ((t - x0) * (y1 - x1) / (y0 - x0) + x1);
}

float RemapFrom01(float t, float x1, float y1)
{
    return (t * (y1 - x1) + x1);
}

float3 NormalizeNormalPerPixel(float3 normalWS)
{
#if defined(UNITY_NO_DXT5nm) && defined(_NORMALMAP)
    return SafeNormalize(normalWS);
#else
    return normalize(normalWS);
#endif
}

uint GetMeshRenderingLayer()
{
    return asuint(unity_RenderingLayer.x);
}

bool IsPerspectiveProjection()
{
    return (unity_OrthoParams.w == 0);
}

float3 GetViewForwardDir()
{
    float4x4 viewMat = GetWorldToViewMatrix();
    return -viewMat[2].xyz;
}

float3 GetCameraPositionWS()
{
    // TODO: Camera Relative Rendering
    return _WorldSpaceCameraPos.xyz;
}

float4 GetScaledScreenParams()
{
    // TODO: Dynamic Resolution _ScaledScreenParams
    return _ScreenParams;
}

uint Select4(uint4 v, uint i)
{
    uint mask0 = uint(int(i << 31) >> 31);
    uint mask1 = uint(int(i << 30) >> 31);
    return
        (((v.w & mask0) | (v.z & ~mask0)) & mask1) |
        (((v.y & mask0) | (v.x & ~mask0)) & ~mask1);
}

void TransformScreenUV(inout float2 uv, float screenHeight)
{
    #if UNITY_UV_STARTS_AT_TOP
    // TODO: Dynamic Resoulutiion _ScaleBiasRt
    uv.y = screenHeight - (uv.y + screenHeight);
    #endif
}

void TransformScreenUV(inout float2 uv)
{
    #if UNITY_UV_STARTS_AT_TOP
    TransformScreenUV(uv, GetScaledScreenParams().y);
    #endif
}

void TransformNormalizedScreenUV(inout float2 uv)
{
    #if UNITY_UV_STARTS_AT_TOP
    TransformScreenUV(uv, 1.0);
    #endif
}

float2 GetNormalizedScreenSpaceUV(float2 positionCS)
{
    float2 normalizedScreenSpaceUV = positionCS.xy * rcp(GetScaledScreenParams().xy);
    TransformNormalizedScreenUV(normalizedScreenSpaceUV);
    return normalizedScreenSpaceUV;
}

float2 GetNormalizedScreenSpaceUV(float4 positionCS)
{
    return GetNormalizedScreenSpaceUV(positionCS.xy);
}

float DistanceAttenuation(float3 lightVector, float range)
{
    range = max(1e-5, range);
    float div = length(lightVector) / range;
    float atten = 1 - div * div;
    return saturate(atten);
}

float AngleAttenuation(float3 spotDirection, float3 lightDirection, float2 spotAttenuation)
{
    float SdotL = dot(spotDirection, lightDirection);
    float atten = saturate((SdotL - spotAttenuation.x) / (spotAttenuation.x - spotAttenuation.y));
    return saturate(atten);
}


#endif