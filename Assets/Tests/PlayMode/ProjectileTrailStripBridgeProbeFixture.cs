#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Tests.PlayMode
{
    // Loads the disposable task-001 proof assets from disk: the compute shader this repo owns,
    // and the fixture VFX graph built by hand in-editor per
    // .agent/projectile-trail-gpu-strips/001-verify-vfx-strip-bridge.md. The graph is never
    // authored or edited by an agent (AgentVFX is read-only; see .agent/vfx-graph.md), so its
    // presence at this path is a precondition the tests fail loudly on instead of skipping.
    internal static class ProjectileTrailStripBridgeProbeFixture
    {
        private const string ComputeShaderPath =
            "Assets/Tests/TestAssets/ProjectileTrailStripBridgeProbe.compute";
        private const string GraphAssetPath =
            "Assets/Tests/TestAssets/ProjectileTrailStripProbe.vfx";

        private static ComputeShader _computeShader;
        private static VisualEffectAsset _graphAsset;

        public static ComputeShader ComputeShader => _computeShader ??= LoadComputeShader();
        public static VisualEffectAsset GraphAsset => _graphAsset ??= LoadGraphAsset();

        private static ComputeShader LoadComputeShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Task-001 probe compute shader not found at '{ComputeShaderPath}'.");
            }

            return shader;
        }

        private static VisualEffectAsset LoadGraphAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GraphAssetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Task-001 probe VFX graph not found at '{GraphAssetPath}'. Build it by hand "
                    + "in-editor per 001-verify-vfx-strip-bridge.md's editor-plan before running "
                    + $"{nameof(ProjectileTrailStripBridgeProbeFixture)}-backed tests.");
            }

            return asset;
        }
    }
}
#endif
