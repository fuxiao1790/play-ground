using PlayGround.Editor.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PlayGround.Editor.Skills
{
    public static class RebuildSkillAtlas
    {
        private const string PrefabFolder = "Assets/Prefabs/Skills";
        private const string AtlasPath = "Assets/Atlas/Skills.spriteatlasv2";

        [MenuItem("Tools/Skills/Rebuild Skill Atlas")]
        public static void Rebuild()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                throw new InvalidOperationException($"Missing skill atlas at '{AtlasPath}'.");
            }

            List<SpriteUse> spriteUses = CollectSpriteUses();
            ValidateSpriteUses(spriteUses);

            List<Sprite> sprites = spriteUses
                .Select(use => use.Sprite)
                .Distinct()
                .OrderBy(SpriteSortKey)
                .ToList();

            atlas = ReplaceAtlasPackables(atlas, sprites);
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            AssertSinglePackedPage(atlas);

            Debug.Log($"Rebuilt skill atlas '{AtlasPath}' with {sprites.Count} sprites from '{PrefabFolder}'.");
            LogAtlasContract();
        }

        [MenuItem("Tools/Skills/Validate Combat Atlas")]
        public static void Validate()
        {
            if (LogAtlasContract() == 0)
            {
                Debug.Log($"Skill atlas '{AtlasPath}' satisfies the combat batch contract.");
            }
        }

        // Same checks the CombatRoot inspector draws, so a repack reports its own violations
        // instead of leaving them for the next play session to render as a full atlas page.
        private static int LogAtlasContract()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            List<CombatAtlasIssue> issues = CombatAtlasValidator.ValidateAtlas(atlas);
            foreach (CombatAtlasIssue issue in issues)
            {
                if (issue.Severity == MessageType.Error)
                {
                    Debug.LogError($"[Combat atlas] {issue.Message}", atlas);
                }
                else
                {
                    Debug.LogWarning($"[Combat atlas] {issue.Message}", atlas);
                }
            }

            return issues.Count;
        }

        private static List<SpriteUse> CollectSpriteUses()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
            if (prefabGuids.Length == 0)
            {
                throw new InvalidOperationException($"No prefabs found under '{PrefabFolder}'.");
            }

            List<SpriteUse> spriteUses = new();

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Failed to load prefab '{prefabPath}'.");
                }

                CollectSpriteUses(prefabPath, prefab, spriteUses);
            }

            return spriteUses;
        }

        private static SpriteAtlas ReplaceAtlasPackables(SpriteAtlas atlas, IReadOnlyList<Sprite> sprites)
        {
            SerializedObject serializedAtlas = AtlasSerializedObject(atlas);
            SerializedProperty packables = FindPackables(serializedAtlas);
            if (packables == null || !packables.isArray)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' exposes neither m_ImporterData.packables nor m_EditorData.packables; Unity SpriteAtlas serialization may have changed.");
            }

            packables.ClearArray();
            for (int i = 0; i < sprites.Count; i++)
            {
                packables.InsertArrayElementAtIndex(i);
                packables.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }

            serializedAtlas.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            return AssertAtlasPackableCount(sprites.Count);
        }

        private static SpriteAtlas AssertAtlasPackableCount(int expectedCount)
        {
            SpriteAtlas reloadedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            SerializedObject serializedAtlas = AtlasSerializedObject(reloadedAtlas);
            SerializedProperty packables = FindPackables(serializedAtlas);
            if (packables == null || !packables.isArray)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' exposes neither m_ImporterData.packables nor m_EditorData.packables after save.");
            }

            if (packables.arraySize != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' save failed: expected {expectedCount} packables, found {packables.arraySize} after import.");
            }

            return reloadedAtlas;
        }

        private static SerializedProperty FindPackables(SerializedObject serializedAtlas)
        {
            return serializedAtlas.FindProperty("m_ImporterData.packables")
                ?? serializedAtlas.FindProperty("m_EditorData.packables");
        }

        private static SerializedObject AtlasSerializedObject(SpriteAtlas atlas)
        {
            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            AssetImporter importer = AssetImporter.GetAtPath(atlasPath);
            return new SerializedObject(importer != null ? importer : atlas);
        }

        private static void AssertSinglePackedPage(SpriteAtlas atlas)
        {
            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            Texture2D[] pages = AssetDatabase.LoadAllAssetsAtPath(atlasPath)
                .OfType<Texture2D>()
                .ToArray();

            if (pages.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' did not produce a packed page. Rebuild failed; do not enter play mode.");
            }

            if (pages.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' produced {pages.Length} packed pages. One combat batch supports one page; increase atlas Max Texture Size or shrink its packables.");
            }
        }

        private static void CollectSpriteUses(string prefabPath, GameObject prefab, List<SpriteUse> spriteUses)
        {
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null)
                {
                    throw new InvalidOperationException($"Prefab '{prefabPath}' has a missing script component.");
                }

                SerializedObject serializedObject = new(component);
                SerializedProperty property = serializedObject.GetIterator();
                bool enterChildren = true;

                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (property.objectReferenceValue is not Sprite sprite)
                    {
                        continue;
                    }

                    spriteUses.Add(new SpriteUse(
                        prefabPath,
                        component,
                        property.propertyPath,
                        sprite,
                        IsUnderVisual(component.transform, prefab.transform)));
                }
            }
        }

        private static void ValidateSpriteUses(List<SpriteUse> spriteUses)
        {
            StringBuilder errors = new();

            if (spriteUses.Count == 0)
            {
                errors.AppendLine($"No sprite references found in prefabs under '{PrefabFolder}'.");
            }

            foreach (SpriteUse use in spriteUses)
            {
                if (!use.IsUnderVisual)
                {
                    errors.AppendLine(
                        $"{use.PrefabPath}: sprite '{use.Sprite.name}' on {use.Component.GetType().Name}.{use.PropertyPath} must be on a component under a child named 'Visual'.");
                }
            }

            IEnumerable<IGrouping<string, Sprite>> duplicateNames = spriteUses
                .Select(use => use.Sprite)
                .Distinct()
                .GroupBy(sprite => sprite.name)
                .Where(group => group.Count() > 1);

            foreach (IGrouping<string, Sprite> group in duplicateNames)
            {
                string locations = string.Join(", ", group.Select(SpriteLocation));
                errors.AppendLine($"Sprite name '{group.Key}' is used by multiple sprite assets: {locations}.");
            }

            if (errors.Length > 0)
            {
                throw new InvalidOperationException($"Skill atlas validation failed:{Environment.NewLine}{errors}");
            }
        }

        private static bool IsUnderVisual(Transform transform, Transform prefabRoot)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current != prefabRoot && current.name == "Visual")
                {
                    return true;
                }
            }

            return false;
        }

        private static string SpriteSortKey(Sprite sprite)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
            {
                return $"{guid}:{localId}";
            }

            return AssetDatabase.GetAssetPath(sprite) + ":" + sprite.name;
        }

        private static string SpriteLocation(Sprite sprite)
        {
            return $"{AssetDatabase.GetAssetPath(sprite)} ({SpriteSortKey(sprite)})";
        }

        private readonly struct SpriteUse
        {
            public SpriteUse(string prefabPath, Component component, string propertyPath, Sprite sprite, bool isUnderVisual)
            {
                PrefabPath = prefabPath;
                Component = component;
                PropertyPath = propertyPath;
                Sprite = sprite;
                IsUnderVisual = isUnderVisual;
            }

            public string PrefabPath { get; }
            public Component Component { get; }
            public string PropertyPath { get; }
            public Sprite Sprite { get; }
            public bool IsUnderVisual { get; }
        }
    }
}
