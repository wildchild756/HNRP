using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    public struct LightData
    {
        /// <summary>
        /// 
        /// </summary>
        public Vector3 positionWS;

        /// <summary>
        /// 0: None
        /// 1: directional light
        /// 2: point light
        /// 3: spot light
        /// </summary>
        public uint lightType;

        /// <summary>
        /// light color
        /// </summary>
        public Vector3 color; 

        /// <summary>
        /// range
        /// </summary>
        public float range;

        /// <summary>
        /// directional light: unused
        /// x: oneOverLightRangeSqr 
        /// y: lightRangeSqrOverFadeRangeSqr
        /// z: invAngleRange
        /// w: add
        /// </summary>
        public Vector4 attenuation;

        /// <summary>
        /// world space direction
        /// </summary>
        public Vector3 directionWS;

        /// <summary>
        /// rendering layer mask
        /// </summary>
        public uint renderingLayerMask;
    }
}
