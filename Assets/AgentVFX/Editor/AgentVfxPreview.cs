using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace AgentVFX.Editor
{
    // Phase 6 preview loop (guide §22). Deliberately independent of
    // AgentVfxInternalBridge: VisualEffect/VisualEffectAsset are Unity's normal
    // public runtime API, not the internal graph-authoring model, so no
    // .asmref/internal access is needed here.
    //
    // Unlike Assets/AgentVFX/InternalAccess/, this file's exact API calls were
    // not re-verified against package source this session (VisualEffect is a
    // native engine-module binding with no local C# source to grep) -- compile-
    // check after the first Editor recompile, same as the CLI command layer.
    public static class AgentVfxPreview
    {
        public static byte[] CapturePreview(string assetPath, float simulateSeconds = 1f, int width = 512, int height = 512)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            if (asset == null)
                throw new InvalidOperationException($"AgentVFX: no VisualEffectAsset at '{assetPath}'.");

            var previewGo = new GameObject("AgentVfxPreview") { hideFlags = HideFlags.HideAndDontSave };
            var cameraGo = new GameObject("AgentVfxPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture renderTexture = null;

            try
            {
                var effect = previewGo.AddComponent<VisualEffect>();
                effect.visualEffectAsset = asset;
                effect.Reinit();

                var camera = cameraGo.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -5f);
                camera.transform.LookAt(previewGo.transform);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;

                renderTexture = new RenderTexture(width, height, 24);
                camera.targetTexture = renderTexture;

                const float step = 1f / 50f;
                var steps = Mathf.Max(1, Mathf.RoundToInt(simulateSeconds / step));
                for (var i = 0; i < steps; i++)
                    effect.Simulate(step);

                camera.Render();

                var previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previousActive;

                var png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                return png;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(previewGo);
                UnityEngine.Object.DestroyImmediate(cameraGo);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
        }
    }
}
