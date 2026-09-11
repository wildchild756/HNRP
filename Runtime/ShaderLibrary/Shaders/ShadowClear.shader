Shader "Hidden/HNRP/ShadowClear"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest Always
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            // 全屏三角形；把深度写到远平面，配合 viewport 局部清空阴影图区域。
            float4 vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                float4 positionCS = GetFullScreenTriangleVertexPosition(vertexID);
#if UNITY_REVERSED_Z
                positionCS.z = 0.0;
#else
                positionCS.z = 1.0;
#endif
                return positionCS;
            }

            void frag()
            {
            }
            ENDHLSL
        }
    }
}
