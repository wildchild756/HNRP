using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    public struct BuildLightDataJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<uint> renderingLayerMasks;
        public NativeArray<LightData> lightDatas;

        public void Execute(int index)
        {
            LightData lightData = new LightData();

            VisibleLight visibleLight = visibleLights[index];
            if (visibleLight == null)
            {
            }
            else
            {
                lightData.positionWS = visibleLight.localToWorldMatrix.GetColumn(3);
                lightData.color = new Vector3(visibleLight.finalColor.r, visibleLight.finalColor.g, visibleLight.finalColor.b);
                lightData.directionWS = -visibleLight.localToWorldMatrix.GetColumn(2);
                lightData.renderingLayerMask = renderingLayerMasks[index];
                
                if(visibleLight.lightType == LightType.Directional)
                {
                    lightData.lightType = 1u;
                }
                else 
                {
                    lightData.range = visibleLight.range;

                    if(visibleLight.lightType == LightType.Spot)
                    {
                        lightData.lightType = 3u;
                        GetSpotLightAttenuation(ref visibleLight, ref lightData);
                    }
                    else if(visibleLight.lightType == LightType.Point)
                    {
                        lightData.lightType = 2u;
                    }
                }
            }

            lightDatas[index] = lightData;
        }


        public static void GetSpotLightAttenuation(ref VisibleLight visibleLight, ref LightData lightData)
        {
            float outerAngle = Mathf.Deg2Rad * visibleLight.spotAngle * 0.5f;
            float innerAngle = outerAngle; // TODO:spot light inner angle
            lightData.attenuation.x = outerAngle;
            lightData.attenuation.y = innerAngle;
            lightData.attenuation.z = Mathf.Cos(outerAngle);
            lightData.attenuation.w = Mathf.Cos(innerAngle);
        }
    }
}
