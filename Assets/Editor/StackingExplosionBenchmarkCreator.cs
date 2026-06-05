using PlayGround.Attack;
using PlayGround.Common.StatusEffects;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Editor
{
    public static class StackingExplosionBenchmarkCreator
    {
        private const string AttacksFolder = "Assets/ScriptableObjects/Attacks";
        private const string StatusEffectsFolder = "Assets/ScriptableObjects/StatusEffects";

        // AOE type id used by both the AoeConfig asset and the hit effect component.
        // Change this if it clashes with an existing AoeConfig in your scene roots.
        private const int ExplosionAoeTypeId = 10;

        [MenuItem("Tools/Play Ground/Create Stacking Explosion Benchmark")]
        public static void Create()
        {
            EnsureFolders();
            CreateTriggerDef();
            CreateExplosionAoeConfig();
            CreateExplosionHitEffect();
            CreateProjectileConfig();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[StackingExplosion] Assets created. Assign basicPrefab fields in the Inspector, then:\n" +
                      "1. Add StatusEffects component to mob prefab.\n" +
                      "2. Assign BenchmarkStackingExplosionEffect to ProjectileAttack.hitEffectDefinitions.\n" +
                      "3. Register BenchmarkExplosionAoeConfig with the player AoeRoot (add to aoeTypes list).\n" +
                      "4. Assign ProjectileAttack.aoeRoot to the player-to-mob AoeRoot.");
        }

        private static void CreateTriggerDef()
        {
            string path = $"{StatusEffectsFolder}/BenchmarkStackingExplosion.asset";
            if (AssetDatabase.LoadAssetAtPath<StackingTriggerDef>(path) != null)
            {
                Debug.Log($"[StackingExplosion] {path} already exists, skipped.");
                return;
            }

            var def = ScriptableObject.CreateInstance<StackingTriggerDef>();
            AssetDatabase.CreateAsset(def, path);

            var so = new SerializedObject(def);
            so.FindProperty("stackThreshold").intValue = 3;
            so.FindProperty("triggerAoeTypeId").intValue = ExplosionAoeTypeId;
            so.FindProperty("triggerAoeDamage").floatValue = 120f;
            so.FindProperty("triggerAoeLifetimeSeconds").floatValue = 0f;
            so.FindProperty("triggerAoeTickIntervalSeconds").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[StackingExplosion] Created {path} (threshold=3, typeId={ExplosionAoeTypeId}, damage=120).");
        }

        private static void CreateExplosionAoeConfig()
        {
            string path = $"{AttacksFolder}/BenchmarkExplosionAoeConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<AoeConfig>(path) != null)
            {
                Debug.Log($"[StackingExplosion] {path} already exists, skipped.");
                return;
            }

            var config = ScriptableObject.CreateInstance<AoeConfig>();
            AssetDatabase.CreateAsset(config, path);

            var so = new SerializedObject(config);
            so.FindProperty("typeId").intValue = ExplosionAoeTypeId;
            so.FindProperty("sizeMultiplier").floatValue = 10f;
            so.FindProperty("damage").floatValue = 120f;
            so.FindProperty("lifetimeSeconds").floatValue = 0f;
            so.FindProperty("count").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[StackingExplosion] Created {path} (typeId={ExplosionAoeTypeId}, damage=120, size=10x). Assign basicPrefab.");
        }

        private static void CreateExplosionHitEffect()
        {
            string path = $"{AttacksFolder}/BenchmarkStackingExplosionEffect.asset";
            if (AssetDatabase.LoadAssetAtPath<ProjectileStatusEffectHitEffectDefinition>(path) != null)
            {
                Debug.Log($"[StackingExplosion] {path} already exists, skipped.");
                return;
            }

            var effect = ScriptableObject.CreateInstance<ProjectileStatusEffectHitEffectDefinition>();
            AssetDatabase.CreateAsset(effect, path);

            var trigger = AssetDatabase.LoadAssetAtPath<StackingTriggerDef>($"{StatusEffectsFolder}/BenchmarkStackingExplosion.asset");
            var so = new SerializedObject(effect);
            so.FindProperty("effectDef").objectReferenceValue = trigger;
            so.FindProperty("stacksPerHit").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[StackingExplosion] Created {path} (applies BenchmarkStackingExplosion stacks).");
        }

        private static void CreateProjectileConfig()
        {
            string path = $"{AttacksFolder}/BenchmarkStackingProjectileConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<ProjectileConfig>(path) != null)
            {
                Debug.Log($"[StackingExplosion] {path} already exists, skipped.");
                return;
            }

            var config = ScriptableObject.CreateInstance<ProjectileConfig>();
            AssetDatabase.CreateAsset(config, path);

            var so = new SerializedObject(config);
            so.FindProperty("speed").floatValue = 300f;
            so.FindProperty("lifetime").floatValue = 2f;
            so.FindProperty("damage").floatValue = 0f;
            so.FindProperty("count").intValue = 9;
            so.FindProperty("spreadDegrees").floatValue = 8f;
            so.FindProperty("jitterDegrees").floatValue = 0f;
            so.FindProperty("targetMask").intValue = 1;
            so.FindProperty("trackingEnabled").boolValue = true;
            so.FindProperty("trackingRange").floatValue = 400f;
            so.FindProperty("trackingTurnSpeedDegrees").floatValue = 720f;
            so.FindProperty("trackingQueryIntervalSeconds").floatValue = 0.05f;
            so.FindProperty("directDamageEnabled").boolValue = false;
            so.FindProperty("impactAoeTypeId").intValue = -1;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[StackingExplosion] Created {path} (9 tracking shots, spread=8, no direct damage). Assign basicPrefab.");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(AttacksFolder))
            {
                AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Attacks");
            }

            if (!AssetDatabase.IsValidFolder(StatusEffectsFolder))
            {
                AssetDatabase.CreateFolder("Assets/ScriptableObjects", "StatusEffects");
            }
        }
    }
}
