using System.Collections.Generic;
using PlayGround.Editor.Skills;
using PlayGround.System.Combat.Authoring;
using PlayGround.System.Combat.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace PlayGround.Editor.Rendering
{
    // Surfaces the combat atlas contract next to the fields that break it. Everything reported
    // here is something CombatRenderResourceRegistry.Register(...) would throw on - or, before
    // those throws existed, silently render as the whole atlas page on every quad.
    [CustomEditor(typeof(CombatRoot))]
    public sealed class CombatRootEditor : UnityEditor.Editor
    {
        private const string SkillAtlasPath = "Assets/Atlas/Skills.spriteatlasv2";

        private readonly List<CombatAtlasIssue> _issues = new();
        private SpriteAtlas _validatedAtlas;
        private MeshRenderer _validatedRenderer;
        private int _validatedSpriteHash;

        private void OnEnable() => Revalidate();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SpriteAtlas atlas = serializedObject.FindProperty("combatSpriteAtlas").objectReferenceValue as SpriteAtlas;
            MeshRenderer renderer = serializedObject.FindProperty("combatSpriteRenderer").objectReferenceValue as MeshRenderer;
            int spriteHash = ReferencedSpriteHash();

            if (atlas != _validatedAtlas || renderer != _validatedRenderer || spriteHash != _validatedSpriteHash)
            {
                Revalidate();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Atlas Contract", EditorStyles.boldLabel);
                if (GUILayout.Button("Revalidate", GUILayout.Width(90f))) Revalidate();
                if (atlas != null
                    && AssetDatabase.GetAssetPath(atlas) == SkillAtlasPath
                    && GUILayout.Button("Rebuild + Repack", GUILayout.Width(130f)))
                {
                    RebuildSkillAtlas.Rebuild();
                    Revalidate();
                }
            }

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Atlas, renderer, and referenced sprites satisfy the combat batch contract.", MessageType.Info);
                return;
            }

            foreach (CombatAtlasIssue issue in _issues)
            {
                EditorGUILayout.HelpBox(issue.Message, issue.Severity);
            }
        }

        private void Revalidate()
        {
            serializedObject.Update();
            SpriteAtlas atlas = serializedObject.FindProperty("combatSpriteAtlas").objectReferenceValue as SpriteAtlas;
            MeshRenderer renderer = serializedObject.FindProperty("combatSpriteRenderer").objectReferenceValue as MeshRenderer;

            _issues.Clear();
            _issues.AddRange(CombatAtlasValidator.ValidateAtlas(atlas));
            _issues.AddRange(CombatAtlasValidator.ValidateRenderer(renderer));
            ValidateReferencedSprites(atlas);

            _validatedAtlas = atlas;
            _validatedRenderer = renderer;
            _validatedSpriteHash = ReferencedSpriteHash();
        }

        // Register(...) throws when GetSprite(name) returns null, and it is name-keyed, so a
        // sprite this component points at must be in the atlas under that exact name.
        private void ValidateReferencedSprites(SpriteAtlas atlas)
        {
            if (atlas == null) return;

            HashSet<string> atlasNames = CombatAtlasValidator.CollectAtlasSpriteNames(atlas);
            foreach (Sprite sprite in ReferencedSprites())
            {
                if (atlasNames.Contains(sprite.name)) continue;

                _issues.Add(new CombatAtlasIssue(
                    MessageType.Error,
                    $"Sprite '{sprite.name}' ({AssetDatabase.GetAssetPath(sprite)}) is referenced by this CombatRoot but is not in atlas "
                    + $"'{atlas.name}'. Add it to the packables and repack."));
            }
        }

        private IEnumerable<Sprite> ReferencedSprites()
        {
            if (serializedObject.FindProperty("projectileSprite").objectReferenceValue is Sprite projectileSprite)
            {
                yield return projectileSprite;
            }

            SerializedProperty renderTypes = serializedObject.FindProperty("renderTypes");
            for (int i = 0; i < renderTypes.arraySize; i++)
            {
                if (renderTypes.GetArrayElementAtIndex(i).FindPropertyRelative("sprite").objectReferenceValue is Sprite sprite)
                {
                    yield return sprite;
                }
            }

            SerializedProperty templates = serializedObject.FindProperty("projectileTemplates");
            for (int i = 0; i < templates.arraySize; i++)
            {
                if (templates.GetArrayElementAtIndex(i).objectReferenceValue is BasicAttackPrefab template
                    && template.Sprite != null)
                {
                    yield return template.Sprite;
                }
            }
        }

        // Cheap change signal so validation reruns when a sprite slot is edited, without
        // re-reading importers every repaint.
        private int ReferencedSpriteHash()
        {
            int hash = 17;
            foreach (Sprite sprite in ReferencedSprites())
            {
                hash = hash * 31 + sprite.GetInstanceID();
            }

            return hash;
        }
    }
}
