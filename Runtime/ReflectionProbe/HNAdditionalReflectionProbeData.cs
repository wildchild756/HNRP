using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    [ExecuteAlways]
    public class HNAdditionalReflectionProbeData : MonoBehaviour, IAdditionalData
    {
        void OnEnable()
        {
            builtinReflectionProbe = GetComponent<ReflectionProbe>();
        }

        void Update()
        {
            
        }


        public ReflectionProbe BuiltinReflectionProbe
        {
            get
            {
                if(!builtinReflectionProbe)
                {
                    gameObject.TryGetComponent<ReflectionProbe>(out builtinReflectionProbe);
                }
                return builtinReflectionProbe;
            }
        }

        public int UpdateCount
        {
            get { return updateCount; }
            set { updateCount = value; }
        }

        /// <summary>
        /// 每探针的渲染图视图索引。
        /// 渲染本探针（实时面渲染与烘焙/自定义烘焙）时，用于从
        /// <see cref="HNRenderPipelineAsset.reflectionRenderGraphViewBlock"/>
        /// 选择使用哪个渲染图视图。
        /// 索引对应渲染图视图键列表中的位置。
        /// </summary>
        public int RenderGraphViewIndex
        {
            get { return renderGraphViewIndex; }
            set { renderGraphViewIndex = value; }
        }


        [SerializeField]
        private ReflectionProbe builtinReflectionProbe;

        [SerializeField]
        private int updateCount = 0;

        [SerializeField]
        private int renderGraphViewIndex = 0;
    }


    public enum ReflectionProbeResolution
    {
        Res256 = 256,
        Res512 = 512,
        Res1024 = 1024,
        Res2048 = 2048,
        Res4096 = 4096,
    }


    public static class ReflectionProbeExtensions
    {
        public static HNAdditionalReflectionProbeData GetHNAdditionalReflectionProbeData(this ReflectionProbe reflectionProbe)
        {
            var gameObject = reflectionProbe.gameObject;
            bool componentExists = gameObject.TryGetComponent<HNAdditionalReflectionProbeData>(out var reflectionProbeData);
            if(!componentExists)
            {
                reflectionProbeData = gameObject.AddComponent<HNAdditionalReflectionProbeData>();
            }
            return reflectionProbeData;
        }
    }
}
