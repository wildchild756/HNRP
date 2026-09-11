#ifndef HNRP_LIT_SHADOW_CASTER_INCLUDED
#define HNRP_LIT_SHADOW_CASTER_INCLUDED

#include "../Common/Common.hlsl"
#include "../Core/Input.hlsl"

// ShadowCaster pass 的最小顶点 / 片元实现：仅把几何体变换到光源裁剪空间，
// 深度写入由 DrawShadows 绑定阴影图完成。深度偏置由绘制侧 SetGlobalDepthBias 承担。

struct ShadowCasterAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShadowCasterVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

ShadowCasterVaryings ShadowCasterVert(ShadowCasterAttributes input)
{
    ShadowCasterVaryings output;
    ZERO_INITIALIZE(ShadowCasterVaryings, output);

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float4 positionCS = TransformWorldToHClip(positionWS);

    // 避免顶点被近平面裁掉，保证贴地阴影不消失。
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

    output.positionCS = positionCS;
    return output;
}

half4 ShadowCasterFrag(ShadowCasterVaryings input) : SV_Target
{
    return 0;
}

#endif
