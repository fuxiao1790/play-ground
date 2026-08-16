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
            AssertRuntimeAtlasContract(atlas);

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
            // V2 inputs live on SpriteAtlasAsset. SerializedObject against either the runtime
            // SpriteAtlas or SpriteAtlasImporter does not expose a supported packable property.
            SpriteAtlasAsset atlasAsset = SpriteAtlasAsset.Load(AtlasPath);
            if (atlasAsset == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load SpriteAtlasAsset inputs from '{AtlasPath}'.");
            }

            UnityEngine.Object[] existingPackables = atlas.GetPackables();
            if (existingPackables is { Length: > 0 })
            {
                atlasAsset.Remove(existingPackables);
            }

            atlasAsset.Add(sprites.Cast<UnityEngine.Object>().ToArray());
            SpriteAtlasAsset.Save(atlasAsset, AtlasPath);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            return AssertAtlasPackableCount(sprites.Count);
        }

        private static SpriteAtlas AssertAtlasPackableCount(int expectedCount)
        {
            SpriteAtlas reloadedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (reloadedAtlas == null)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' failed to reload after save.");
            }

            UnityEngine.Object[] savedPackables = reloadedAtlas.GetPackables();
            int actualCount = savedPackables?.Length ?? 0;
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' save failed: expected {expectedCount} packables, found {actualCount} after import.");
            }

            return reloadedAtlas;
        }

        private static void AssertRuntimeAtlasContract(SpriteAtlas atlas)
        {
            List<string> errors = CombatAtlasValidator.ValidateAtlas(atlas)
                .Where(issue => issue.Severity == MessageType.Error)
                .Select(issue => issue.Message)
                .ToList();

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Skill atlas '{AtlasPath}' violates runtime render requirements:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, errors));
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
