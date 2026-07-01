using PlayGround.Skills.Runtime;
using PlayGround.System.Common;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.Skills
{
    public static class SkillSpawnTranslator
    {
        public static void Spawn(
            RuntimeSkillDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            Vector2 aimWorldPos,
            CombatRoot combatRoot,
            CombatFaction faction)
        {
            if (def == null || combatRoot == null || IsDefault(def.SpawnTemplateKey))
            {
                return;
            }

            if (def is RuntimeProjectileDefinition projectile)
            {
                if (projectile.Prefab == null || projectile.TypeId < 0)
                {
                    return;
                }

                combatRoot.SpawnRegisteredProjectile(
                    projectile.SpawnTemplateKey,
                    origin,
                    aimDir,
                    Mathf.Max(1, projectile.Count),
                    faction);
                return;
            }

            if (def is RuntimeAoeDefinition aoe)
            {
                if (aoe.TypeId < 0)
                {
                    return;
                }

                combatRoot.SpawnRegisteredAoe(
                    aoe.SpawnTemplateKey,
                    aimWorldPos,
                    Mathf.Max(1, aoe.EchoCount),
                    faction);
            }
        }

        private static bool IsDefault(Hash128 key) => key.Equals(default(Hash128));
    }
}
