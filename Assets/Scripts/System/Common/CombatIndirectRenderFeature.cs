using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PlayGround.System.Common
{
    // Static handoff between the ECS render system and the URP render pass.
    //
    // The immediate-mode Graphics.RenderMeshIndirect API is NOT executed by URP's 2D Renderer
    // (confirmed via Frame Debugger: the draw never enters the Renderer2D pass). The only reliable
    // way to land an indirect draw is to record it from inside a ScriptableRenderPass. So
    // CombatBatchedRenderSystem prepares the buffers/material on the main thread and publishes them
    // here; CombatIndirectRenderFeature reads them and issues the draw inside the 2D pass. Mirrors
    // the CombatVfxRoot.Instance handoff pattern already used by the VFX dispatch system.
    public static class CombatIndirectRenderData
    {
        public static Mesh Mesh;
        public static Material Material;
        public static GraphicsBuffer ArgsBuffer;
        public static bool HasWork;

        public static void Publish(Mesh mesh, Material material, GraphicsBuffer argsBuffer)
        {
            Mesh = mesh;
            Material = material;
            ArgsBuffer = argsBuffer;
            HasWork = mesh != null && material != null && argsBuffer != null;
        }

        public static void Clear()
        {
            HasWork = false;
            Mesh = null;
            Material = null;
            ArgsBuffer = null;
        }
    }

    // Add this feature to the active Renderer2D asset (Assets/Settings/Renderer2D.asset) via the
    // Inspector: Add Renderer Feature -> Combat Indirect Render Feature.
    public class CombatIndirectRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;

        private CombatIndirectRenderPass _pass;

        public override void Create()
        {
            _pass = new CombatIndirectRenderPass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (CombatIndirectRenderData.HasWork)
                renderer.EnqueuePass(_pass);
        }

        private sealed class CombatIndirectRenderPass : ScriptableRenderPass
        {
            private sealed class PassData
            {
                public Mesh Mesh;
                public Material Material;
                public GraphicsBuffer ArgsBuffer;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!CombatIndirectRenderData.HasWork
                    || CombatIndirectRenderData.Mesh == null
                    || CombatIndirectRenderData.Material == null
                    || CombatIndirectRenderData.ArgsBuffer == null)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid())
                    return;

                using IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass<PassData>("Combat Indirect Sprites", out PassData passData);

                passData.Mesh = CombatIndirectRenderData.Mesh;
                passData.Material = CombatIndirectRenderData.Material;
                passData.ArgsBuffer = CombatIndirectRenderData.ArgsBuffer;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawMeshInstancedIndirect(data.Mesh, 0, data.Material, 0, data.ArgsBuffer);
                });
            }
        }
    }
}
