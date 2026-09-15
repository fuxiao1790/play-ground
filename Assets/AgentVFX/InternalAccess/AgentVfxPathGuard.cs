using System;

namespace AgentVFX.Internal
{
    // Graph inspection is intentionally confined to authored VFX assets. This
    // bridge is read-only; future mutation support needs a separate review.
    internal static class AgentVfxPathGuard
    {
        private const string AllowedRoot = "Assets/Vfx/";

        public static void EnsureAllowed(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("Asset path must not be empty.", nameof(assetPath));

            var normalized = assetPath.Replace('\\', '/');
            if (normalized.Contains("../") || normalized.Contains("/./") ||
                normalized.EndsWith("/.", StringComparison.Ordinal) || normalized.EndsWith("/..", StringComparison.Ordinal))
                throw new InvalidOperationException($"AgentVFX: path traversal is not allowed: {assetPath}");

            // Case-insensitive: Windows/macOS filesystems are case-preserving but
            // not case-sensitive, so exact-case validation is a trap.
            if (normalized.StartsWith(AllowedRoot, StringComparison.OrdinalIgnoreCase) &&
                normalized.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                return;

            throw new InvalidOperationException(
                "AgentVFX may only inspect .vfx assets under Assets/Vfx/. " +
                $"Rejected path: {assetPath}");
        }
    }
}
