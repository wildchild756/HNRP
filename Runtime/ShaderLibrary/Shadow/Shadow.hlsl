#ifndef HNRP_SHADOW_INCLUDED
#define HNRP_SHADOW_INCLUDED

#include "../Common/Common.hlsl"
#include "../Core/Input.hlsl"

// ── GPU 数据布局（须与 C# DrawShadowPass.cs 一致）──

/// 按 light 的阴影元数据（两级查表第一级）。
struct ShadowLightData
{
    int    lightIndex;      // = buffer 下标
    int    resolution;      // 单张 map 分辨率；0 = 该 light 无阴影
    uint2  blockDatas;      // map 0..7 的 atlas 位置，每张 8 位 = (slice << 6) | blockId
    float4 cascadeSplits0;  // 方向光 cascade split 0..3（世界空间；0 表示无该级）
    float4 cascadeSplits1;  // 方向光 cascade split 4..7
};

/// 按 atlas 槽的阴影数据（两级查表第二级）。buffer 下标 = atlas 位置字段。
struct ShadowMapData
{
    float4   shadowParams;   // x=strength, y=soft, z=cascadeIndex/faceIndex
    float4x4 worldToShadow;  // 世界 → 阴影裁剪（含 [0,1] remap，不含 atlas offset）
    float4   cullingSphere;  // xyz=球心，w=半径平方（方向光选级）
};

GLOBAL_CBUFFER_START(_ShadowMapParamsBuffer, b1)
    float4 _ShadowGlobalParams;   // x=mainLightIndex, y=lightCount, z=方向光数, w=本地光数
    float4 _ShadowGlobalParams2;  // x=shadowDistance, y=sliceResolution, zw=预留
CBUFFER_END

#define _SHADOW_MAIN_LIGHT_INDEX (_ShadowGlobalParams.x)
#define _SHADOW_LIGHT_COUNT      (_ShadowGlobalParams.y)
#define _SHADOW_SLICE_RESOLUTION (_ShadowGlobalParams2.y)

#if defined(SHADOW_MAP)
StructuredBuffer<ShadowLightData> _ShadowLightDatas;
StructuredBuffer<ShadowMapData> _ShadowMapDatas;
TEXTURE2D_ARRAY_SHADOW(_ShadowMapArray);
SAMPLER_CMP(sampler_LinearClampCompare);
#endif

// ── 两级查表 ──

/// 取该 light 第 k 张 map 的 8 位 atlas 位置（同时是 ShadowMapData 下标）。
uint GetShadowMapIndex(ShadowLightData lightData, int k)
{
    uint word = lightData.blockDatas[k >> 2];
    return (word >> ((k & 3) * 8)) & 0xFFu;
}

/// 方向光有效 cascade 数：由非零 split 数推导（pass 会将未用级清零）。
int GetDirectionalCascadeCount(ShadowLightData lightData)
{
    int count = 0;
    float splits[8] =
    {
        lightData.cascadeSplits0.x, lightData.cascadeSplits0.y,
        lightData.cascadeSplits0.z, lightData.cascadeSplits0.w,
        lightData.cascadeSplits1.x, lightData.cascadeSplits1.y,
        lightData.cascadeSplits1.z, lightData.cascadeSplits1.w,
    };
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        if (splits[i] > 0.0)
        {
            count++;
        }
    }
    return count;
}

#if defined(SHADOW_MAP)
/// 由 atlas 位置字段解出采样所需的 scaleOffset 与 slice。
void DecodeShadowPosition(uint field, int resolution, out float4 scaleOffset, out int sliceIndex)
{
    uint blockId = field & 0x3Fu;
    sliceIndex = int(field >> 6);

    float perAxis = max(1.0, _SHADOW_SLICE_RESOLUTION / 512.0);
    float scale = resolution / _SHADOW_SLICE_RESOLUTION;

    uint xId = 0u;
    uint yId = 0u;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        xId |= ((blockId >> (i * 2)) & 1u) << i;
        yId |= ((blockId >> (i * 2 + 1)) & 1u) << i;
    }

    scaleOffset = float4(scale, scale, xId / perAxis, yId / perAxis);
}

float SampleShadowMap(int resolution, uint mapIndex, float3 positionWS)
{
    ShadowMapData mapData = _ShadowMapDatas[mapIndex];

    float4 scaleOffset;
    int sliceIndex;
    DecodeShadowPosition(mapIndex, resolution, scaleOffset, sliceIndex);

    float4 shadowCoord = mul(mapData.worldToShadow, float4(positionWS, 1.0));
    if (shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0)
    {
        return 1.0;
    }

    float2 uv = shadowCoord.xy * scaleOffset.xy + scaleOffset.zw;
    float attenuation = SAMPLE_TEXTURE2D_ARRAY_SHADOW(
        _ShadowMapArray, sampler_LinearClampCompare, float3(uv, shadowCoord.z), sliceIndex);

    return lerp(1.0, attenuation, saturate(mapData.shadowParams.x));
}
#endif

/// 计算某光源对世界坐标点的阴影衰减（1 = 无阴影）。
float GetShadowAttenuation(uint lightIndex, float3 positionWS, float3 lightDirectionWS)
{
#if defined(SHADOW_MAP)
    ShadowLightData lightData = _ShadowLightDatas[lightIndex];
    if (lightData.resolution <= 0)
    {
        return 1.0;
    }

    int lightType = (int)(_LightDatasBuffer[lightIndex].lightType);
    uint mapIndex = 0u;

    if (lightType == 2 /* Point */)
    {
        int face = CubeMapFaceID(-lightDirectionWS);
        mapIndex = GetShadowMapIndex(lightData, face);
    }
    else if (lightType == 1 /* Directional */)
    {
        int cascadeCount = GetDirectionalCascadeCount(lightData);
        if (cascadeCount <= 0)
        {
            return 1.0;
        }

        int found = -1;
        [loop]
        for (int k = 0; k < cascadeCount; k++)
        {
            uint candidate = GetShadowMapIndex(lightData, k);
            ShadowMapData mapData = _ShadowMapDatas[candidate];
            float3 diff = positionWS - mapData.cullingSphere.xyz;
            if (dot(diff, diff) < mapData.cullingSphere.w)
            {
                found = (int)candidate;
                break;
            }
        }

        if (found < 0)
        {
            return 1.0;
        }

        mapIndex = (uint)found;
    }
    else /* Spot */
    {
        mapIndex = GetShadowMapIndex(lightData, 0);
    }

    return SampleShadowMap(lightData.resolution, mapIndex, positionWS);
#else
    return 1.0;
#endif
}

#endif
