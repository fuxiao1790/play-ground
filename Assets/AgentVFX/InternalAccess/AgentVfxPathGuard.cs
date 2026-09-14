using System;

namespace AgentVFX.Internal
{
    // Confines every AgentVFX mutation to a narrow, agent-generated-content area of
    // the project instead of trusting a caller-supplied path. Guide §25.
    internal static class AgentVfxPathGuard
    {
        // "Assets/Vfx/" matches this project's actual convention
        // (Assets/Vfx/LineSeg, Assets/Vfx/Aoe/...) -- confirmed against real
        // content, not assumed. "Assets/AgentGenerated/" is scratch space for
        // graphs the agent creates from nothing.
        private static readonly string[] AllowedRoots =
        {
            "Assets/Vfx/",
            "Assets/AgentGenerated/",
        };

        public static void EnsureAllowed(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("Asset path must not be empty.", nameof(assetPath));

            var normalized = assetPath.Replace('\\', '/');
            foreach (var root in AllowedRoots)
            {
                // Case-insensitive: Windows/macOS filesystems are case-preserving
                // but not case-sensitive, so an exact-case guard is a trap for a
                // caller who typed a path with different (still valid) casing.
                if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            throw new InvalidOperationException(
                "AgentVFX may only read or write assets under Assets/Vfx/ or Assets/AgentGenerated/. " +
                $"Rejected path: {assetPath}");
        }
    }
}
