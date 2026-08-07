using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using UnityEngine;
using Unity.Entities;
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
            CombatFaction faction,
            Entity caster = default,
            int castToken = 0)
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
                    faction,
                    caster,
                    Mathf.Max(0f, projectile.ManaCost),
                    castToken);
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
                    faction,
                    AoeVariant.AoeChildKindFor(aoe.LifetimeSeconds),
                    caster,
                    Mathf.Max(0f, aoe.ManaCost),
                    castToken);
                return;
            }

            if (def is RuntimeTargetedDefinition targeted)
            {
                if (targeted.TypeId < 0 || targeted.SpawnBlocked)
                {
                    return;
                }

                combatRoot.SpawnRegisteredTargeted(
                    targeted.SpawnTemplateKey,
                    origin,
                    aimWorldPos,
                    Mathf.Max(1, targeted.Count),
                    faction,
                    TargetedVariant.ChildKindFor(targeted.LifetimeSeconds),
                    caster,
                    Mathf.Max(0f, targeted.ManaCost),
                    castToken);
            }
        }

        private static bool IsDefault(Hash128 key) => key.Equals(default(Hash128));
    }
}
