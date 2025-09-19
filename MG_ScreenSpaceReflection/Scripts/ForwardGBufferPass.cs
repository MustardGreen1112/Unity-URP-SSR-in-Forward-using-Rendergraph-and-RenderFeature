using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class ForwardGBufferPass : ScriptableRenderPass
{
    private int _passIndex;
    private String _targetName;
    private RTHandle _customTarget;
    private int _ssrThinGBufferId = Shader.PropertyToID("_SsrThinGBuffer");
    private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
    private FilteringSettings m_filteringSettings;
    private Shader _thinGbufferShader;

    public ForwardGBufferPass(string passName)
    {
        profilingSampler = new ProfilingSampler(passName);
        m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
        m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
        m_ShaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));

        m_filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        if (_thinGbufferShader == null)
        {
            _thinGbufferShader = Shader.Find("SSR/ThinGBuffer");
        }
    }

    public void Dispose()
    {
        _customTarget?.Release();
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<ForwardGBufferPassData>(passName, out var passData, new ProfilingSampler("Thin GBuffer RenderGraph")))
        {
            // Access the relevant frame data from the Universal Render Pipeline
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = SortingCriteria.CommonOpaque;
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, universalRenderingData, cameraData, lightData, sortFlags);
            drawSettings.overrideShader = _thinGbufferShader;

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, m_filteringSettings);
            passData.RendererListHandle = renderGraph.CreateRendererList(param);

            RenderTextureDescriptor desc = new RenderTextureDescriptor(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height);
            desc.colorFormat = RenderTextureFormat.RG16;
            //use depth pre-pass 
            desc.depthBufferBits = 0;
            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SsrThinGBuffer", true);
            passData.Destination = destination;

            builder.UseRendererList(passData.RendererListHandle);
            builder.SetRenderAttachment(passData.Destination, 0);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((ForwardGBufferPassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.RendererListHandle);
            });

            builder.SetGlobalTextureAfterPass(passData.Destination, _ssrThinGBufferId);
        }
    }

    private class ForwardGBufferPassData
    {
        internal RendererListHandle RendererListHandle;
        internal TextureHandle Destination;
    }
}
