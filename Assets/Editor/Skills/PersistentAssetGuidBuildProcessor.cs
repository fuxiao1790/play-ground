using System.Collections.Generic;
using PlayGround.Common;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PlayGround.Editor
{
    public sealed class PersistentAssetGuidBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BakeAll();
        }

        [MenuItem("PlayGround/Skills/Bake Persistent Asset GUIDs")]
        public static void BakeAll()
        {
            var guids = new HashSet<string>();
            AddGuids(guids, "t:Skill");
            AddGuids(guids, "t:SkillSupport");
            AddGuids(guids, "t:TriggerLink");
            bool changed = false;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PersistentScriptableObject asset =
                    AssetDatabase.LoadAssetAtPath<PersistentScriptableObject>(path);
                if (asset != null)
                {
                    changed |= asset.BakeAssetGuid();
                }
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static void AddGuids(HashSet<string> destination, string filter)
        {
            string[] found = AssetDatabase.FindAssets(filter);
            for (int i = 0; i < found.Length; i++)
            {
                destination.Add(found[i]);
            }
        }
    }
}
