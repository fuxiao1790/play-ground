using System.Linq;
using UnityEditor.VFX;
using UnityEngine;

namespace AgentVFX.Internal
{
    // vfx_compile, vfx_errors (guide §19) -- a real compile+diagnose loop instead
    // of console scraping.
    public static partial class AgentVfxInternalBridge
    {
        public static string Compile(string assetPath)
        {
            var graph = OpenGraph(assetPath);

            // Mirrors Unity's own compile-and-report sequence
            // (VFXGraphPreprocessor.OnCompileResource).
            graph.errorManager.RefreshCompilationReport();
            graph.SetExpressionValueDirty();
            graph.CompileForImport();
            graph.errorManager.GenerateErrors();

            var errors = CollectErrors(graph);
            var success = errors.All(e => e.severity != nameof(VFXErrorType.Error));

            return JsonUtility.ToJson(new CompileResultSnapshot { success = success, errors = errors });
        }

        public static string GetErrors(string assetPath)
        {
            var graph = OpenGraph(assetPath);
            var errors = CollectErrors(graph);
            return JsonUtility.ToJson(new CompileResultSnapshot
            {
                success = errors.All(e => e.severity != nameof(VFXErrorType.Error)),
                errors = errors,
            });
        }

        private static ErrorSnapshot[] CollectErrors(VFXGraph graph)
        {
            var reporter = graph.errorManager.compileReporter;
            if (reporter == null)
                return System.Array.Empty<ErrorSnapshot>();

            return reporter.dirtyModels
                .SelectMany(model => reporter.GetDirtyModelErrors(model)
                    .Select(report => new ErrorSnapshot
                    {
                        nodeId = AgentVfxIdMap.GetOrCreateId(report.model),
                        severity = report.type.ToString(),
                        code = report.error,
                        message = report.description,
                    }))
                .ToArray();
        }
    }
}
