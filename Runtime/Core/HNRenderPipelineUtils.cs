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
    }
}
