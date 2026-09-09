using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using System.Linq;

namespace HN.HNRP
{
    [Pass(PassNameConst)]
    public sealed class DrawShadowPass : Pass
    {
        public const string PassNameConst = "Draw Shadow";


        [SerializeField]
        private TextureResourceParams shadowMapParams;

        [SerializeField]
        private RendererListParams rendererListParams;


        public int ShadowmapResolution
        {
            get => shadowMapParams.Width;
            set
            {
                shadowMapParams.Width = value;
                shadowMapParams.Height = value;
            }
        }

        public int ShadowmapSliceCount
        {
            get => shadowMapParams.Slices;
            set => shadowMapParams.Slices = value;
        }

        public uint RenderingLayerMask
        {
            get => rendererListParams.RenderingLayerMask;
            set => rendererListParams.RenderingLayerMask = value;
        }


        public TextureSlot ShadowMapOutputSlot { get; private set; }


        private CameraContext cameraContext;

        private TextureAllocator textureAllocator;

        private List<LightParamsData> lightParamsList = new List<LightParamsData>();

        private List<MapData> mapDatas;
        
        /// <summary>
        /// texture allocator当前帧需要绘制或重新分配的结果。字典中第一个元素是需要绘制的结果，其他结果是需要重新分配的结果。
        /// </summary>
        private Dictionary<uint, TextureAllocatorResult> allocateResults;

        private List<uint> resultMapIds;


        public DrawShadowPass(string passName)
            : base(passName)
        {
            mapDatas = new List<MapData>();
            allocateResults = new Dictionary<uint, TextureAllocatorResult>();
            resultMapIds = new List<uint>();
            textureAllocator = new TextureAllocator(ShadowmapResolution, 512, 4096, ShadowmapSliceCount);
        }

        public override void SetupSlots()
        {
            ShadowMapOutputSlot = new TextureSlot("drawShadowMapOutput", SlotDirection.Output);
            RegisterSlot(ShadowMapOutputSlot);
        }

        public override void PreRecord(RenderGraphAsset template, CameraContext context)
        {
            this.cameraContext = context;

            shadowMapParams = new TextureResourceParams
            {
                ColorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.Shadow),
                Width = ShadowmapResolution,
                Height = ShadowmapResolution,
                DepthBits = DepthBits.Depth32,
                Slices = ShadowmapSliceCount,
                TextureScale = Vector2.one,
                FilterMode = FilterMode.Bilinear,
                WrapMode = TextureWrapMode.Clamp,
                TextureDimension = TextureDimension.Tex2DArray,
                ClearBuffer = true,
                ClearColor = Color.black
            };

            rendererListParams = new RendererListParams
            {
                ListKind = RenderListKind.Opaque,
                RenderingLayerMask = 0x00000001
            };

            for(int i = 0; i < context.VisibleLights.Length; i++)
            {
                var light = context.VisibleLights[i];
                if(light.light.TryGetComponent(out HNAdditionalLightData additionalLightData))
                {
                    if(additionalLightData.EnableShadow)
                    {
                        lightParamsList.Add(new LightParamsData
                        {
                            lightIndex = i,
                            lightType = light.lightType,
                            lightDirection = light.localToWorldMatrix.GetColumn(2),
                            cascadeCount = additionalLightData.CascadeCount,
                            cascadeResolution = additionalLightData.CascadeResolution,
                            cascadeSplits = additionalLightData.CascadeSplits,
                            shadowUpdateMode = additionalLightData.ShadowUpdateMode,
                            cascadeTimeSlices = additionalLightData.CascadeTimeSlices
                        });
                    }
                }
            }

            mapDatas.Clear();
        }

        public override void Record(RenderGraph renderGraph)
        {
            if(cameraContext == null)
            {
                IsEnabled &= false;
            }

            TextureHandle shadowMapHandle = renderGraph.CreateTexture(shadowMapParams.CreateDesc("DirectionalShadowMap", cameraContext.Camera));

            RendererListHandle rendererList = CreateRendererList(renderGraph);

            if(ShadowMapOutputSlot != null)
            {
                ShadowMapOutputSlot.SetHandle(shadowMapHandle);
            }

            using var builder = renderGraph.AddRenderPass<DrawShadowPassData>(
                PassName, out var passData);
            
            builder.AllowPassCulling(false);

            passData.shadowMap = builder.WriteTexture(shadowMapHandle);
            passData.rendererList = builder.UseRendererList(rendererList);

            var camera = cameraContext.Camera;
            for(int i = 0; i < lightParamsList.Count; i++)
            {
                if(lightParamsList[i].lightType == LightType.Directional)
                {
                    float maxDistance = lightParamsList[i].cascadeSplits[(int)lightParamsList[i].cascadeCount - 1];
                    float[] cascadeSplits01 = new float[(int)lightParamsList[i].cascadeCount];
                    for(int j = 0; j < (int)lightParamsList[i].cascadeCount; j++)
                    {
                        cascadeSplits01[j] = lightParamsList[i].cascadeSplits[j] / maxDistance;
                    }

                    Vector3[] nearCorners = new Vector3[4];
                    Vector3[] farCorners = new Vector3[4];
                    camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.nearClipPlane, camera.stereoActiveEye, nearCorners);
                    camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), maxDistance, camera.stereoActiveEye, farCorners);
                    for(int j = 0; j < 4; j++)
                    {
                        nearCorners[j] = camera.transform.TransformPoint(nearCorners[j]);
                        farCorners[j] = camera.transform.TransformPoint(farCorners[j]);
                    }

                    Vector3 lightDir = -lightParamsList[i].lightDirection;
                    Vector3 lightPos = camera.transform.position;
                    Quaternion lightRotation = Quaternion.LookRotation(lightDir, Vector3.up);
                    Matrix4x4 lightViewMatrix = Matrix4x4.TRS(lightPos, lightRotation, Vector3.one).inverse;

                    for(int j = 0; j < (int)lightParamsList[i].cascadeCount; j++)
                    {
                        float nearPlane = j == 0 ? camera.nearClipPlane : lightParamsList[i].cascadeSplits[j - 1];
                        float farPlane = lightParamsList[i].cascadeSplits[j];

                        Vector3[] cascadeCorners = new Vector3[8];
                        for(int k = 0; k < 4; k++)
                        {
                            cascadeCorners[k] = Vector3.Lerp(nearCorners[k], farCorners[k], (nearPlane - camera.nearClipPlane) / (maxDistance - camera.nearClipPlane));
                            cascadeCorners[k + 4] = Vector3.Lerp(nearCorners[k], farCorners[k], (farPlane - camera.nearClipPlane) / (maxDistance - camera.nearClipPlane));
                        }

                        CalculateBoundingSphere(cascadeCorners, out Vector3 cascadeCenter, out float cascadeRadius);
                        uint mapIndex = ((uint)lightParamsList[i].lightIndex << 4) | (uint)j;
                        var shadowSplitData = new ShadowSplitData()
                        {
                            cullingSphere = new Vector4(cascadeCenter.x, cascadeCenter.y, cascadeCenter.z, cascadeRadius),
                        };
                        mapDatas.Add(new MapData(){
                            LightType = LightType.Directional,
                            LightIndex = lightParamsList[i].lightIndex,
                            MapIndex = mapIndex,
                            ShadowSplitData = shadowSplitData,
                            Resolution = lightParamsList[i].resolution
                        });
                    }
                }
            }

            builder.SetRenderFunc((DrawShadowPassData data, RenderGraphContext ctx) =>
            {
                if(!IsEnabled)
                {
                    return;
                }

                foreach(var mapData in mapDatas)
                {
                    textureAllocator.Allocate(ref allocateResults, mapData.MapIndex, mapData.Resolution.x);

                    resultMapIds = allocateResults.Keys.ToList();
                    for(int i = allocateResults.Count - 1; i != 0; i--)
                    {
                        var result = allocateResults[resultMapIds[i]];
                        if(result.IsReorg)
                        {
                            ctx.cmd.CopyTexture(
                                shadowMapHandle, result.OldSliceIndex, 0, 
                                (int)result.OldScaleOffset.z * ShadowmapResolution, 
                                (int)result.OldScaleOffset.w * ShadowmapResolution, 
                                (int)result.OldScaleOffset.x * ShadowmapResolution, 
                                (int)result.OldScaleOffset.y * ShadowmapResolution,
                                shadowMapHandle, result.SliceIndex, 0, 
                                (int)result.ScaleOffset.z * ShadowmapResolution, 
                                (int)result.ScaleOffset.w * ShadowmapResolution
                            );
                        }
                        else
                        {
                            var projectionType = mapData.LightType == LightType.Directional ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective;
                            ShadowDrawingSettings settings = new ShadowDrawingSettings(cameraContext.CullingResults, mapData.LightIndex, projectionType)
                            {
                                splitData = mapData.ShadowSplitData
                            };
                            ctx.cmd.SetViewport(new Rect(result.OldScaleOffset.z, result.OldScaleOffset.w, result.OldScaleOffset.x, result.OldScaleOffset.y));
                            ctx.renderContext.DrawShadows(ref settings);
                        }
                    }
                    resultMapIds.Clear();
                }

                allocateResults.Clear();
            });
        }

        public override void Cleanup()
        {
            
        }


        private RendererListHandle CreateRendererList(RenderGraph renderGraph)
        {
            if (!cameraContext.HasCullingResults)
            {
                return default;
            }

            RendererListDesc desc = rendererListParams.CreateDesc(
                ShaderPassNames.AllShadowCasterNames,
                cameraContext.CullingResults,
                cameraContext.Camera);
            return renderGraph.CreateRendererList(desc);
        }

        private void CalculateBoundingSphere(Vector3[] points, out Vector3 center, out float radius)
        {
            // 取所有点的AABB中心，半径取中心到最远点的距离
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            for(int i = 1; i < points.Length; i++)
            {
                bounds.Encapsulate(points[i]);
            }
            center = bounds.center;
            radius = bounds.extents.magnitude;
        }


        private sealed class DrawShadowPassData
        {
            public TextureHandle shadowMap;

            public RendererListHandle rendererList;
        }


        private struct LightParamsData
        {
            public int lightIndex;

            public LightType lightType;

            public Vector3 lightDirection;

            public HNAdditionalLightData.CascadeCountType cascadeCount;

            public HNAdditionalLightData.ResolutionType cascadeResolution;

            public List<float> cascadeSplits;

            public HNAdditionalLightData.ShadowUpdateModeType shadowUpdateMode;

            public List<int> cascadeTimeSlices;
            
            public Vector2Int resolution;
        }

        private struct MapData
        {
            public LightType LightType;

            public int LightIndex;

            /// 0位-3位（4位）: cascade index(directional light) face index(point light)
            /// > 4位: lightIndex
            public uint MapIndex;

            public ShadowSplitData ShadowSplitData;

            public Vector2Int Resolution;
        }
    }


}
