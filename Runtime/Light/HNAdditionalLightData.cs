using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class HNAdditionalLightData : MonoBehaviour, IAdditionalData
    {
        void OnEnable()
        {
            builtinLight = GetComponent<Light>();
        }

        void Update()
        {
            
        }


        public Light BuiltinLight
        {
            get
            {
                if(!builtinLight)
                {
                    gameObject.TryGetComponent<Light>(out builtinLight);
                }
                return builtinLight;
            }
        }

        public Vector2 LightCookieSize
        {
            get => lightCookieSize;
            set => lightCookieSize = value;
        }

        public Vector2 LightCookieOffset
        {
            get => lightCookieOffset;
            set => lightCookieOffset = value;
        }

        public uint RenderingLayerMask
        {
            get => renderingLayerMask;
            set => renderingLayerMask = value;
        }

        public bool EnableShadow
        {
            get => enableShadow;
            set => enableShadow = value;
        }

        public CascadeCountType CascadeCount
        {
            get => cascadeCount;
            set => cascadeCount = value;
        }

        public ResolutionType CascadeResolution
        {
            get => cascadeResolution;
            set => cascadeResolution = value;
        }

        public List<float> CascadeSplits
        {
            get => cascadeSplits;
            set => cascadeSplits = value;
        }

        public ShadowUpdateModeType ShadowUpdateMode
        {
            get => shadowUpdateMode;
            set => shadowUpdateMode = value;
        }

        public List<int> CascadeTimeSlices
        {
            get => cascadeTimeSlices;
            set => cascadeTimeSlices = value;
        }


        [SerializeField]
        private Light builtinLight;

        [SerializeField]
        private Vector2 lightCookieSize = Vector2.one;

        [SerializeField]
        private Vector2 lightCookieOffset = Vector2.zero;

        [SerializeField]
        private uint renderingLayerMask = 1;

        [SerializeField]
        private bool enableShadow = true;

        [SerializeField]
        private CascadeCountType cascadeCount = CascadeCountType.Four;

        [SerializeField]
        private ResolutionType cascadeResolution = ResolutionType.Medium;

        [SerializeField]
        private List<float> cascadeSplits = new List<float>((int)CascadeCountType.Eight)
        {
            1f, 4f, 10f, 30f, 50f, 100f, 300f, 1000f
        };

        [SerializeField]
        private ShadowUpdateModeType shadowUpdateMode = ShadowUpdateModeType.EveryFrame;

        [SerializeField]
        private List<int> cascadeTimeSlices = new List<int>((int)CascadeCountType.Eight)
        {
            0, 0, 2, 2, 4, 4, 8, 8
        };


        public enum CascadeCountType
        {
            One = 1,
            Two = 2,
            Four = 4,
            Eight = 8
        }

        public enum ResolutionType
        {
            Low = 512,
            Medium = 1024,
            High = 2048,
            Ultra = 4096
        }

        public enum ShadowUpdateModeType
        {
            EveryFrame,
            OnDemand,
            Custom
        }
    }


    public static class LightExtensions
    {
        public static HNAdditionalLightData GetHNRPAdditionalLightData(this Light light)
        {
            var gameObject = light.gameObject;
            bool componentExists = gameObject.TryGetComponent<HNAdditionalLightData>(out var lightData);
            if (!componentExists)
            {
                lightData = gameObject.AddComponent<HNAdditionalLightData>();
            }
            return lightData;
        }
    }
}
