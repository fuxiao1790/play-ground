using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace AgentVFX.Internal
{
    // JSON encoding for VFX settings and slots. Curves, gradients, and Unity
    // object references use explicit read-only representations.
    internal static class AgentVfxJson
    {
        public static string ToJson(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case float f: return f.ToString(CultureInfo.InvariantCulture);
                case double d: return d.ToString(CultureInfo.InvariantCulture);
                case string s: return EscapeJsonString(s);
                case Vector2 v2: return JsonUtility.ToJson(v2);
                case Vector3 v3: return JsonUtility.ToJson(v3);
                case Vector4 v4: return JsonUtility.ToJson(v4);
                case Color c: return JsonUtility.ToJson(c);
                case Enum e: return EscapeJsonString(e.ToString());
                case AnimationCurve curve: return JsonUtility.ToJson(new CurveValue
                {
                    preWrapMode = curve.preWrapMode.ToString(),
                    postWrapMode = curve.postWrapMode.ToString(),
                    keys = System.Array.ConvertAll(curve.keys, key => new CurveKeyValue
                    {
                        time = key.time,
                        value = key.value,
                        inTangent = key.inTangent,
                        outTangent = key.outTangent,
                        inWeight = key.inWeight,
                        outWeight = key.outWeight,
                        weightedMode = key.weightedMode.ToString(),
                    }),
                });
                case Gradient gradient: return JsonUtility.ToJson(new GradientValue
                {
                    colorKeys = System.Array.ConvertAll(gradient.colorKeys, key => new GradientColorKeyValue
                    {
                        color = key.color,
                        time = key.time,
                    }),
                    alphaKeys = System.Array.ConvertAll(gradient.alphaKeys, key => new GradientAlphaKeyValue
                    {
                        alpha = key.alpha,
                        time = key.time,
                    }),
                    mode = gradient.mode.ToString(),
                });
                case UnityEngine.Object unityObject: return JsonUtility.ToJson(new AssetValue
                {
                    assetPath = AssetDatabase.GetAssetPath(unityObject),
                    typeName = unityObject.GetType().FullName,
                    name = unityObject.name,
                });
                default:
                    return JsonUtility.ToJson(new OpaqueValue { typeName = value.GetType().FullName });
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null)
                return "null";

            var escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return "\"" + escaped + "\"";
        }

        [System.Serializable] private sealed class CurveValue { public string preWrapMode; public string postWrapMode; public CurveKeyValue[] keys; }
        [System.Serializable] private sealed class CurveKeyValue { public float time; public float value; public float inTangent; public float outTangent; public float inWeight; public float outWeight; public string weightedMode; }
        [System.Serializable] private sealed class GradientValue { public GradientColorKeyValue[] colorKeys; public GradientAlphaKeyValue[] alphaKeys; public string mode; }
        [System.Serializable] private sealed class GradientColorKeyValue { public Color color; public float time; }
        [System.Serializable] private sealed class GradientAlphaKeyValue { public float alpha; public float time; }
        [System.Serializable] private sealed class AssetValue { public string assetPath; public string typeName; public string name; }
        [System.Serializable] private sealed class OpaqueValue { public string typeName; }
    }
}
