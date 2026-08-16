using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
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

            List<UnityEngine.Object> packables = ReadPackables(atlas, atlasPath, issues);
            if (packables == null) return issues;

            if (packables.Count == 0)
            {
                issues.Add(new CombatAtlasIssue(MessageType.Error, $"Atlas '{atlas.name}' has no packables."));
                return issues;
            }

            List<Sprite> sprites = ResolveSprites(packables, issues);
            if (sprites.Count == 0)
            {
                issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Atlas '{atlas.name}' resolves no sprites from its packables."));
                return issues;
            }

            ValidateMeshTypes(sprites, issues);
            ValidateUniqueNames(sprites, issues);
            ValidateRuntimeSprites(atlas, sprites, issues);
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

        // Use Unity's public packable API. SpriteAtlas V2 does not support reading its importer
        // inputs through SerializedObject, and its private property layout changes across versions.
        private static List<UnityEngine.Object> ReadPackables(
            SpriteAtlas atlas,
            string atlasPath,
            List<CombatAtlasIssue> issues)
        {
            UnityEngine.Object[] packables;
            try
            {
                packables = atlas.GetPackables();
            }
            catch (Exception exception)
            {
                issues?.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Failed to read packables from atlas '{atlasPath}': {exception.GetType().Name}: {exception.Message}"));
                return null;
            }

            if (packables == null)
            {
                issues?.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Atlas '{atlasPath}' returned a null packable list."));
                return null;
            }

            List<UnityEngine.Object> result = new(packables.Length);
            for (int i = 0; i < packables.Length; i++)
            {
                UnityEngine.Object packable = packables[i];
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

        // Exercise the same SpriteAtlas API and packed Sprite properties that Register(...) uses
        // at runtime. Importer packables and generated page sub-assets can look valid while this
        // lookup still returns null, an unpacked Sprite, a Tight mesh, or a different page.
        private static void ValidateRuntimeSprites(
            SpriteAtlas atlas,
            List<Sprite> sprites,
            List<CombatAtlasIssue> issues)
        {
            Texture atlasPage = null;

            foreach (Sprite sourceSprite in sprites.Distinct())
            {
                Sprite packedSprite = null;
                try
                {
                    packedSprite = atlas.GetSprite(sourceSprite.name);
                    if (packedSprite == null)
                    {
                        issues.Add(new CombatAtlasIssue(
                            MessageType.Error,
                            $"Sprite '{sourceSprite.name}' is listed by atlas '{atlas.name}' but GetSprite returned null. "
                            + "Repack the atlas before entering play mode."));
                        continue;
                    }

                    if (!packedSprite.packed)
                    {
                        issues.Add(new CombatAtlasIssue(
                            MessageType.Error,
                            $"Sprite '{packedSprite.name}' came back from atlas '{atlas.name}' unpacked; its UVs still address source texture "
                            + $"'{(packedSprite.texture != null ? packedSprite.texture.name : "<null>")}', so the quad would sample the whole bound atlas page. "
                            + "Repack the atlas before entering play mode."));
                        continue;
                    }

                    Texture packedPage = packedSprite.texture;
                    if (packedPage == null)
                    {
                        issues.Add(new CombatAtlasIssue(
                            MessageType.Error,
                            $"Sprite '{packedSprite.name}' came back from atlas '{atlas.name}' packed but has no texture page."));
                        continue;
                    }

                    if (atlasPage == null)
                    {
                        atlasPage = packedPage;
                    }
                    else if (atlasPage != packedPage)
                    {
                        issues.Add(new CombatAtlasIssue(
                            MessageType.Error,
                            $"Sprite '{packedSprite.name}' packed onto atlas page '{packedPage.name}' but '{atlasPage.name}' is already used; "
                            + $"atlas '{atlas.name}' spilled onto multiple pages. One draw call binds one page - raise Max Texture Size or shrink the packables."));
                    }

                    int vertexCount = packedSprite.vertices.Length;
                    if (vertexCount != 4)
                    {
                        issues.Add(new CombatAtlasIssue(
                            MessageType.Error,
                            $"Sprite '{packedSprite.name}' has a {vertexCount}-vertex (Tight) packed mesh; "
                            + "the combat atlas basis needs vertices at the rect corners. Set its texture importer Mesh Type to Full Rect and repack."));
                    }
                }
                catch (Exception exception)
                {
                    issues.Add(new CombatAtlasIssue(
                        MessageType.Error,
                        $"Runtime atlas lookup for sprite '{sourceSprite.name}' threw {exception.GetType().Name}: {exception.Message}"));
                }
                finally
                {
                    if (packedSprite != null)
                    {
                        UnityEngine.Object.DestroyImmediate(packedSprite);
                    }
                }
            }
        }
    }
}
