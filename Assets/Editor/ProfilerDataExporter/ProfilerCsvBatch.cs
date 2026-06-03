using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ProfilerDataExporter
{
    public static class ProfilerCsvBatch
    {
        private const string CapturesFolder = "ProfilerCaptures";

        [MenuItem("Window/Profiler Data Exporter/Convert All .data in ProfilerCaptures")]
        public static void ConvertAllMenuItem() => ConvertAll();

        // Entry point for: Unity.exe -batchmode -executeMethod ProfilerDataExporter.ProfilerCsvBatch.ConvertAll
        public static void ConvertAll()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var capturesDir = Path.Combine(projectRoot, CapturesFolder);

            if (!Directory.Exists(capturesDir))
            {
                Debug.LogError($"ProfilerCsvBatch: folder not found: {capturesDir}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var dataFiles = Directory.GetFiles(capturesDir, "*.data");
            if (dataFiles.Length == 0)
            {
                Debug.LogWarning($"ProfilerCsvBatch: no .data files in {capturesDir}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            int ok = 0;
            foreach (var dataFile in dataFiles)
            {
                var csvPath = Path.ChangeExtension(dataFile, ".csv");
                if (ConvertFile(dataFile, csvPath))
                    ok++;
            }

            Debug.Log($"ProfilerCsvBatch: {ok}/{dataFiles.Length} files converted.");
            if (Application.isBatchMode) EditorApplication.Exit(ok == dataFiles.Length ? 0 : 1);
        }

        private static bool ConvertFile(string dataPath, string csvPath)
        {
            ProfilerDriver.LoadProfile(dataPath, false);

            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first)
            {
                Debug.LogError($"ProfilerCsvBatch: no frames loaded from {dataPath}");
                return false;
            }

            var data = ProfilerData.GetProfilerData(first, last);
            File.WriteAllText(csvPath, data.ToCsv());
            data.Clear();
            Debug.Log($"ProfilerCsvBatch: {Path.GetFileName(dataPath)} → {Path.GetFileName(csvPath)}  ({last - first + 1} frames)");
            return true;
        }
    }
}
