using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace PlayGround.Editor.Rendering
{
    public readonly struct CombatAtlasIssue
    {
        public CombatAtlasIssue(MessageType severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public MessageType Severity { get; }
        public string Message { get; }
    }

    // Edit-time mirror of the constraints CombatRenderResourceRegistry.Register enforces by
    // throwing at runtime. Each check here maps to one of those throws, so a clean report means
    // registration will not blow up - or silently draw the whole atlas page on every quad.
    // See Docs/reference/simulation/combat-render-system.md "Critical Constraints".
    public static class CombatAtlasValidator
    {
        public const string RequiredShader = "Combat/AtlasIndirectSprite";
        public const string RequiredSortingLayer = "CombatSprites";

        public static List<CombatAtlasIssue> ValidateAtlas(SpriteAtlas atlas)
        {
            List<CombatAtlasIssue> issues = new();
            if (atlas == null)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    "No Combat Sprite Atlas assigned. Register(...) throws on the first skill that needs a sprite."));
                return issues;
            }

            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            if (string.IsNullOrEmpty(atlasPath))
            {
                issues.Add(new CombatAtlasIssue(MessageType.Error, $"Atlas '{atlas.name}' is not a project asset."));
                return issues;
            }

            ValidatePages(atlasPath, issues);

            List<UnityEngine.Object> packables = ReadPackables(atlas, atlasPath, issues);
            if (packables == null) return issues;

            if (packables.Count == 0)
            {
                issues.Add(new CombatAtlasIssue(MessageType.Error, $"Atlas '{atlas.name}' has no packables."));
                return issues;
            }

            List<Sprite> sprites = ResolveSprites(packables, issues);
            ValidateMeshTypes(sprites, issues);
            ValidateUniqueNames(sprites, issues);
            return issues;
        }

        // GetSprite(...) is name-keyed, so the atlas set is the lookup table Register(...) sees.
        public static HashSet<string> CollectAtlasSpriteNames(SpriteAtlas atlas)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            if (atlas == null) return names;

            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            if (string.IsNullOrEmpty(atlasPath)) return names;

            List<UnityEngine.Object> packables = ReadPackables(atlas, atlasPath, null);
            if (packables == null) return names;

            foreach (Sprite sprite in ResolveSprites(packables, null))
            {
                names.Add(sprite.name);
            }

            return names;
        }

        public static List<CombatAtlasIssue> ValidateRenderer(MeshRenderer renderer)
        {
            List<CombatAtlasIssue> issues = new();
            if (renderer == null)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    "No Combat Sprite Renderer assigned. AttachRenderer never runs, so Register(...) has no Material to bind."));
                return issues;
            }

            if (renderer.GetComponent<MeshFilter>() == null)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"'{renderer.name}' has no MeshFilter; the capacity mesh cannot be attached."));
            }

            Material material = renderer.sharedMaterial;
            if (material == null)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"'{renderer.name}' has no Material. Assign one using the {RequiredShader} shader."));
            }
            else if (material.shader == null || material.shader.name != RequiredShader)
            {
                string shaderName = material.shader != null ? material.shader.name : "<none>";
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Material '{material.name}' uses shader '{shaderName}'; the combat batch needs '{RequiredShader}'."));
            }

            ValidateSortingLayer(renderer, issues);
            return issues;
        }

        // MeshRenderer.sortingLayerName is not exposed in the Inspector, so a SortingGroup is the
        // normal way to place this batch. When one is present it is what positions the renderer,
        // and the MeshRenderer's own field stays at whatever it was - reading that field alone
        // reports a correctly configured renderer as misplaced.
        private static void ValidateSortingLayer(MeshRenderer renderer, List<CombatAtlasIssue> issues)
        {
            SortingGroup group = renderer.GetComponentInParent<SortingGroup>(true);
            string effectiveLayer = group != null ? group.sortingLayerName : renderer.sortingLayerName;
            if (effectiveLayer == RequiredSortingLayer) return;

            string source = group != null
                ? $"Sorting Group on '{group.name}'"
                : $"'{renderer.name}' (no Sorting Group; MeshRenderer.sortingLayerName is not editable in the Inspector, so add one)";

            issues.Add(new CombatAtlasIssue(
                MessageType.Warning,
                $"{source} places the combat batch on Sorting Layer '{effectiveLayer}', not '{RequiredSortingLayer}'. "
                + "The whole batch sorts as one Renderer against actor sprites and combat VFX."));
        }

        private static void ValidatePages(string atlasPath, List<CombatAtlasIssue> issues)
        {
            Texture2D[] pages = AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Texture2D>().ToArray();
            if (pages.Length == 0)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    "Atlas has no packed page texture. GetSprite(...) hands back unpacked sprites whose UVs still address their "
                    + "source texture, so a Full Rect single PNG draws the entire bound page on its quad. Repack the atlas."));
                return;
            }

            if (pages.Length > 1)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Atlas packed onto {pages.Length} pages. One draw call binds one page - raise Max Texture Size or shrink the packables."));
            }
        }

        // Matches the serialization RebuildSkillAtlas writes through: V2 keeps packables on the
        // SpriteAtlasAsset under m_ImporterData, V1 under m_EditorData.
        private static List<UnityEngine.Object> ReadPackables(
            SpriteAtlas atlas,
            string atlasPath,
            List<CombatAtlasIssue> issues)
        {
            SerializedObject serializedAtlas = AtlasSerializedObject(atlas);
            SerializedProperty packables = serializedAtlas.FindProperty("m_ImporterData.packables")
                ?? serializedAtlas.FindProperty("m_EditorData.packables");

            if (packables == null || !packables.isArray)
            {
                issues?.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Atlas '{atlasPath}' exposes neither m_ImporterData.packables nor m_EditorData.packables; "
                    + "Unity SpriteAtlas serialization may have changed."));
                return null;
            }

            List<UnityEngine.Object> result = new(packables.arraySize);
            for (int i = 0; i < packables.arraySize; i++)
            {
                UnityEngine.Object packable = packables.GetArrayElementAtIndex(i).objectReferenceValue;
                if (packable == null)
                {
                    issues?.Add(new CombatAtlasIssue(
                        MessageType.Error,
                        $"Packable {i} is a missing reference. Re-add the sprite and repack."));
                    continue;
                }

                result.Add(packable);
            }

            return result;
        }

        private static SerializedObject AtlasSerializedObject(SpriteAtlas atlas)
        {
            string atlasPath = AssetDatabase.GetAssetPath(atlas);
            AssetImporter importer = AssetImporter.GetAtPath(atlasPath);
            return new SerializedObject(importer != null ? importer : atlas);
        }

        private static List<Sprite> ResolveSprites(
            List<UnityEngine.Object> packables,
            List<CombatAtlasIssue> issues)
        {
            List<Sprite> sprites = new();
            foreach (UnityEngine.Object packable in packables)
            {
                switch (packable)
                {
                    case Sprite sprite:
                        sprites.Add(sprite);
                        break;

                    case Texture2D texture:
                        {
                            string path = AssetDatabase.GetAssetPath(texture);
                            List<Sprite> subSprites = SpritesAt(path);
                            sprites.AddRange(subSprites);
                            if (subSprites.Count > 1)
                            {
                                issues?.Add(new CombatAtlasIssue(
                                    MessageType.Warning,
                                    $"Packable '{texture.name}' is the whole texture, so all {subSprites.Count} of its sprites pack. "
                                    + "Reference the individual Sprite sub-assets instead to keep the page small."));
                            }
                            break;
                        }

                    case DefaultAsset folder when AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)):
                        {
                            string path = AssetDatabase.GetAssetPath(folder);
                            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { path }))
                            {
                                sprites.AddRange(SpritesAt(AssetDatabase.GUIDToAssetPath(guid)));
                            }
                            break;
                        }

                    default:
                        issues?.Add(new CombatAtlasIssue(
                            MessageType.Warning,
                            $"Packable '{packable.name}' is a {packable.GetType().Name}; expected a Sprite, Texture2D, or folder."));
                        break;
                }
            }

            return sprites;
        }

        private static List<Sprite> SpritesAt(string assetPath)
        {
            List<Sprite> sprites = new();
            if (string.IsNullOrEmpty(assetPath)) return sprites;

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is Sprite sprite) sprites.Add(sprite);
            }

            return sprites;
        }

        // ComputeUvBasis treats the extreme vertices as rect corners. A Tight hull puts them
        // inside the rect, so the basis skews the sprite. This is the check that catches it
        // before the art ever reaches a quad.
        private static void ValidateMeshTypes(List<Sprite> sprites, List<CombatAtlasIssue> issues)
        {
            if (issues == null) return;

            Dictionary<string, List<string>> tightByPath = new(StringComparer.Ordinal);
            foreach (Sprite sprite in sprites)
            {
                string path = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(path)) continue;
                if (tightByPath.ContainsKey(path))
                {
                    tightByPath[path].Add(sprite.name);
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                TextureImporterSettings settings = new();
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType == SpriteMeshType.FullRect) continue;

                tightByPath[path] = new List<string> { sprite.name };
            }

            foreach (KeyValuePair<string, List<string>> pair in tightByPath)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"'{pair.Key}' has Mesh Type = Tight ({pair.Value.Count} sprite(s) in the atlas). "
                    + "The UV basis needs vertices at the rect corners; set Mesh Type = Full Rect and repack."));
            }
        }

        private static void ValidateUniqueNames(List<Sprite> sprites, List<CombatAtlasIssue> issues)
        {
            if (issues == null) return;

            foreach (IGrouping<string, Sprite> group in sprites
                .Distinct()
                .GroupBy(sprite => sprite.name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                string locations = string.Join(", ", group.Select(AssetDatabase.GetAssetPath).Distinct());
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Sprite name '{group.Key}' is used by {group.Count()} atlas entries ({locations}). "
                    + "Register(...) resolves sprites by name, so it cannot tell them apart."));
            }
        }
    }
}
