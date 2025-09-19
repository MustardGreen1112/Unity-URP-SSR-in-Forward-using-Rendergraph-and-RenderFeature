using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class MgSSR : ScriptableRendererFeature
{
    class MgSSRPass : ScriptableRenderPass
    {
        internal SSRSettings Settings { get; set; }
        const string m_PassName = "MgSSRPass";
        const string k_DepthTextureName = "_CameraDepthTexture";
        const string k_NormalsTextureName = "_CameraNormalsTexture";
        const int linearPass = 0;
        const int compositePass = 1;
        readonly int _ssrThinGBufferId = Shader.PropertyToID("_SsrThinGBuffer");
        //const int hizPass = 2;
        private RenderTextureDescriptor m_descriptor;
        public void Setup(SSRSettings settings)
        {
            requiresIntermediateTexture = true;
            Settings = settings;
            // Configure normal for forward(+) rendering.
            this.ConfigureInput(ScriptableRenderPassInput.Normal);
        }
        public class SSRPassData
        {
            internal TextureHandle depthTexture;
            internal Material material;
            internal TextureHandle cameraNormalsTexture;
            internal TextureHandle reflectedUV;
        }
        public class CopyPassData
        {
            internal TextureHandle src;
            internal TextureHandle dst;
        }
        public class SSRCompositePassData
        {
            internal Material material;
            internal TextureHandle reflectedUV;
            internal TextureHandle src;
        }
        void ExecuteSSRPass(SSRPassData data, RasterGraphContext context, int pass)
        {
            if (data.material == null) { Debug.LogError("Cannot find material during executing pass."); return; }
            data.material.SetTexture(k_DepthTextureName, data.depthTexture);
            data.material.SetTexture(k_NormalsTextureName, data.cameraNormalsTexture);
            data.material.SetInt(Shader.PropertyToID("_numSteps"), Settings.m_maxSteps);
            data.material.SetFloat(Shader.PropertyToID("_thickness"), Settings.m_thickness);
            data.material.SetFloat(Shader.PropertyToID("_stepSize"), Settings.m_stepSize);
            Blitter.BlitTexture(context.cmd, Vector2.one, data.material, pass);
        }

        void ExecuteCompositePass(SSRCompositePassData data, RasterGraphContext context, int pass)
        {
            data.material.SetTexture("_reflectedScreenSpaceUV", data.reflectedUV);
            data.material.SetTexture("_MainTex", data.src);
            data.material.SetFloat("_reflectedIntensity", Settings.m_reflectedIntensity);
            data.material.SetFloat("_edgeFade", Settings.m_edgeFade);
            Blitter.BlitTexture(context.cmd, Vector2.one, data.material, pass);
        }

        static void ExecuteCopyPass(TextureHandle sourceRT, RasterGraphContext context)
        {
            Blitter.BlitTexture(context.cmd, sourceRT, Vector2.one, 0.0f, false);
        }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Fetch data from frameData and cameraData.
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            RenderTextureDescriptor copyDescriptor = cameraData.cameraTargetDescriptor;
            copyDescriptor.depthBufferBits = 0;
            TextureHandle activeColorTexture_cp = UniversalRenderer.CreateRenderGraphTexture(renderGraph, copyDescriptor, "SSR_CopyColorTexture", false);
            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("CopyColor", out var passData))
            {
                passData.dst = activeColorTexture_cp;
                passData.src = resourceData.activeColorTexture;
                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) => { ExecuteCopyPass(passData.src, context); });
            }
            // Create RT.   
            // Set the descriptor for the render texture we want to create.
            m_descriptor = cameraData.cameraTargetDescriptor;
            m_descriptor.useMipMap = false;
            m_descriptor.autoGenerateMips = false;
            m_descriptor.width = cameraData.cameraTargetDescriptor.width;
            m_descriptor.height = cameraData.cameraTargetDescriptor.height;
            m_descriptor.depthBufferBits = 0;
            m_descriptor.colorFormat = RenderTextureFormat.RGB111110Float;
            TextureHandle reflectedUV = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_descriptor, "SSR_ReflectedUVMap", false);
            using (var builder = renderGraph.AddRasterRenderPass<SSRPassData>(m_PassName, out var passData))
            {
                // Set the pass's material. 
                passData.material = Settings.m_ssrMaterial;
                passData.depthTexture = resourceData.activeDepthTexture;
                passData.cameraNormalsTexture = resourceData.cameraNormalsTexture;
                // Declare input data for the pass.
                builder.UseTexture(passData.cameraNormalsTexture, AccessFlags.Read);
                builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                // Set render target attachment. 
                builder.SetRenderAttachment(reflectedUV, 0);
                builder.SetRenderFunc((SSRPassData data, RasterGraphContext context) => { ExecuteSSRPass(data, context, linearPass); });
            }
            using (var builder = renderGraph.AddRasterRenderPass<SSRCompositePassData>(m_PassName + "Composite", out var passData))
            {
                passData.reflectedUV = reflectedUV;
                passData.src = activeColorTexture_cp;
                passData.material = Settings.m_ssrMaterial;
                // Declare input data for the pass.
                builder.UseTexture(passData.reflectedUV, AccessFlags.Read);
                builder.UseTexture(passData.src, AccessFlags.Read);
                // Set render target attachment. 
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderFunc((SSRCompositePassData data, RasterGraphContext context) => { ExecuteCompositePass(data, context, compositePass); });
            }
        }
    }

    public enum TracingModes
    {
        LinearTracing = 0,
        HiZTracing = 1,
    }

    /// Properties shows up in the Editor. 
    [Tooltip("The event where to inject the pass.")]
    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    public int maxSteps;
    public float thichness;
    public float stepSize = 0.1f;
    [Range(0.0f, 1.0f)]
    public float intensity = 0.1f;
    [Range(0.0f, 10.0f)]
    public float edgeFade = 1.0f;
    public TracingModes tracingmode;

    /// <summary>
    /// Settings passed into and used by render pass. 
    /// </summary>
    class SSRSettings
    {
        internal RenderPassEvent m_renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        internal Shader m_ssrShader;
        internal Material m_ssrMaterial;
        internal int m_maxSteps = 50;
        internal float m_thickness = 0.1f;
        internal float m_stepSize = 0.1f;
        internal float m_reflectedIntensity = 0.1f;
        internal float m_edgeFade = 1.0f;
        internal TracingModes m_tracingMode = TracingModes.HiZTracing;

        public bool InitMaterial()
        {
            if (m_ssrMaterial != null)
            {
                return true;
            }
            if (m_ssrShader == null)
            {
                m_ssrShader = Shader.Find("Hidden/mg_ssr_shader");
                if (m_ssrShader == null) return false;
            }
            m_ssrMaterial = CoreUtils.CreateEngineMaterial(m_ssrShader);
            return m_ssrMaterial != null;
        }
    }

    /// <summary>
    /// Render pass used by this feature.
    /// </summary>
    MgSSRPass m_Pass;
    SSRSettings m_Settings;
    ForwardGBufferPass m_forwardGBufferPass;

    // Here you can create passes and do the initialization of them. This is called everytime serialization happens.
    public override void Create()
    {
        m_Pass = new MgSSRPass();
        m_Pass.renderPassEvent = injectionPoint;
        m_Settings = new SSRSettings
        {
            m_renderPassEvent = injectionPoint,
            m_ssrShader = Shader.Find("Hidden/mg_ssr_shader"),
            m_maxSteps = maxSteps,
            m_thickness = thichness,
            m_stepSize = stepSize,
            m_tracingMode = tracingmode,
            m_reflectedIntensity = intensity,
            m_edgeFade = edgeFade
        };
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!m_Settings.InitMaterial()) { Debug.LogError("Could not create ssr material instance."); return; }
        m_Pass.Setup(m_Settings);
        renderer.EnqueuePass(m_Pass);
        m_forwardGBufferPass = new ForwardGBufferPass("Forward GBuffer Pass for SSR");
        //m_forwardGBufferPass.ConfigureInput(ScriptableRenderPassInput.Normal);
        m_forwardGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        renderer.EnqueuePass(m_forwardGBufferPass);
    }
}
