using System;
using System.Globalization;
using UnityEngine;

namespace AgentVFX.Internal
{
    // Minimal JSON<->value conversion for setting/slot values. Covers the common
    // VFX setting/slot value kinds (bool, numeric, string, enum, and the small set
    // of Unity math structs VFX slots commonly expose). Deliberately not a general
    // JSON<->arbitrary-CLR-type mapper -- unsupported types throw with a clear
    // message rather than silently guessing. Extend case-by-case as real usage
    // hits an unsupported type.
    internal static class AgentVfxJson
    {
        public static object FromJson(string json, Type targetType)
        {
            if (targetType == typeof(bool)) return JsonUtility.FromJson<BoolBox>(Wrap(json)).value;
            if (targetType == typeof(int)) return JsonUtility.FromJson<IntBox>(Wrap(json)).value;
            if (targetType == typeof(float)) return JsonUtility.FromJson<FloatBox>(Wrap(json)).value;
            if (targetType == typeof(double)) return double.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (targetType == typeof(string)) return JsonUtility.FromJson<StringBox>(Wrap(json)).value;
            if (targetType == typeof(Vector2)) return JsonUtility.FromJson<Vector2>(json);
            if (targetType == typeof(Vector3)) return JsonUtility.FromJson<Vector3>(json);
            if (targetType == typeof(Vector4)) return JsonUtility.FromJson<Vector4>(json);
            if (targetType == typeof(Color)) return JsonUtility.FromJson<Color>(json);

            if (targetType.IsEnum)
            {
                var raw = JsonUtility.FromJson<StringBox>(Wrap(json)).value;
                return Enum.Parse(targetType, raw, ignoreCase: true);
            }

            throw new NotSupportedException(
                $"AgentVFX: no JSON conversion registered for setting/slot type '{targetType.FullName}'. " +
                "Supported: bool, int, float, double, string, enum, Vector2/3/4, Color.");
        }

        // Bare JSON literals -- symmetric with FromJson, which itself unwraps a bare
        // scalar via Wrap() before parsing. A round trip (ToJson then FromJson) must
        // see the same textual shape it can also accept back in from an agent.
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
                default:
                    // Best-effort fallback for plain serializable structs (e.g. some
                    // VFX slot value types); returns "{}" if Unity can't serialize it.
                    return JsonUtility.ToJson(value);
            }
        }

        private static string Wrap(string rawScalarJson) => "{\"value\":" + rawScalarJson + "}";

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

        [Serializable] private sealed class BoolBox { public bool value; }
        [Serializable] private sealed class IntBox { public int value; }
        [Serializable] private sealed class FloatBox { public float value; }
        [Serializable] private sealed class StringBox { public string value; }
    }
}
