using System.Collections;
using System.Collections.Generic;
using GluonGui.Dialog;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace HN.HNRP
{
    public static class HNRenderPipelineUtils
    {
        unsafe public static void GetVisibleLight(NativeArray<VisibleLight> visibleLights, int index, ref VisibleLight result)
        {
            result = UnsafeUtility.ArrayElementAsRef<VisibleLight>(visibleLights.GetUnsafePtr(), index);
        }

        public static bool IsProbeGreater(VisibleReflectionProbe probe, VisibleReflectionProbe otherProbe)
        {
            return probe.importance < otherProbe.importance ||
                (probe.importance == otherProbe.importance && probe.bounds.extents.sqrMagnitude > otherProbe.bounds.extents.sqrMagnitude);
        }

        public static void FilterReflectionProbe(ref NativeArray<VisibleReflectionProbe> reflectionProbes, int reflectionProbeCount)
        {
            for(int i = 1; i < reflectionProbeCount; i++)
            {
                var probe = reflectionProbes[i];
                var j = i - 1;
                while (j >= 0 && IsProbeGreater(reflectionProbes[j], probe))
                {
                    reflectionProbes[j + 1] = reflectionProbes[j];
                    j--;
                }
                reflectionProbes[j + 1] = probe;
            }
        }

        public static void ValidateComputeBuffer(ref ComputeBuffer computeBuffer, int size, int stride, ComputeBufferType type = ComputeBufferType.Default)
        {
            if (computeBuffer == null || computeBuffer.count < size)
            {
                CoreUtils.SafeRelease(computeBuffer);
                computeBuffer = new ComputeBuffer(size, stride, type);
            }
        }


        /// <summary>
        /// 从可见光列表选出 main light 索引：优先 <see cref="RenderSettings.sun"/>，
        /// 否则取最亮的方向光；无方向光时返回 -1。
        /// </summary>
        /// <param name="visibleLights">相机可见光数组。</param>
        /// <returns>main light 的索引，找不到时为 -1。</returns>
        public static int GetMainLightIndex(NativeArray<VisibleLight> visibleLights)
        {
            int visibleLightsCount = visibleLights.Length;
            if (visibleLightsCount == 0)
            {
                return -1;
            }

            Light sunLight = RenderSettings.sun;
            int brightestDirectionalLightIndex = -1;
            float brightestLightIntensity = 0.0f;
            for (int i = 0; i < visibleLightsCount; i++)
            {
                VisibleLight currVisibleLight = visibleLights[i];
                Light currLight = currVisibleLight.light;

                // 粒子系统 light 的 light 属性为 null，且排在 visibleLights 末尾。
                if (currLight == null)
                {
                    break;
                }

                if (currVisibleLight.lightType == LightType.Directional)
                {
                    if (currLight == sunLight)
                    {
                        return i;
                    }

                    if (currLight.intensity > brightestLightIntensity)
                    {
                        brightestLightIntensity = currLight.intensity;
                        brightestDirectionalLightIndex = i;
                    }
                }
            }

            return brightestDirectionalLightIndex;
        }


        /// <summary>
        /// 把单张阴影 map 的请求分辨率收敛到 atlas 单 slice 分辨率以内（不低于 512）。
        /// </summary>
        /// <param name="resolution">请求分辨率（2 的幂）。</param>
        /// <param name="atlasResolution">atlas 单 slice 分辨率。</param>
        /// <returns>可用的 map 分辨率。</returns>
        public static int ClampShadowResolution(int resolution, int atlasResolution)
        {
            int clamped = Mathf.Clamp(resolution, 512, Mathf.Max(512, atlasResolution));
            return Mathf.ClosestPowerOfTwo(clamped);
        }


        public static readonly string PREVIEW_CAMERA_NAME = "Preview Camera";

        public static readonly string PREVIEW_SCENE_CAMERA_NAME = "Preview Scene Camera";
    }
}
