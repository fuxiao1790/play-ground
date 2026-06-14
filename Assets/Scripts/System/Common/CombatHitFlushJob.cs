using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Common
{
    [BurstCompile]
    public struct CombatHitFlushJob : IJob
    {
        public NativeStream PendingDamage;
        public NativeStream PendingSpawns;
        public BufferLookup<CombatDamageElement> Damage;
        public BufferLookup<CombatSpawnElement> Spawns;

        public void Execute()
        {
            NativeStream.Reader damageReader = PendingDamage.AsReader();
            for (int i = 0; i < damageReader.ForEachCount; i++)
            {
                int count = damageReader.BeginForEachIndex(i);
                for (int j = 0; j < count; j++)
                {
                    WriteDamage(damageReader.Read<CombatPendingDamage>());
                }

                damageReader.EndForEachIndex();
            }

            NativeStream.Reader spawnReader = PendingSpawns.AsReader();
            for (int i = 0; i < spawnReader.ForEachCount; i++)
            {
                int count = spawnReader.BeginForEachIndex(i);
                for (int j = 0; j < count; j++)
                {
                    WriteSpawn(spawnReader.Read<CombatPendingSpawn>());
                }

                spawnReader.EndForEachIndex();
            }
        }

        private void WriteDamage(CombatPendingDamage pending)
        {
            if (pending.Scope == Entity.Null || !Damage.HasBuffer(pending.Scope))
            {
                return;
            }

            Damage[pending.Scope].Add(new CombatDamageElement
            {
                SourceId = pending.SourceId,
                TypeId = pending.TypeId,
                TargetId = pending.TargetId,
                Position = pending.Position,
                Kind = pending.Kind,
                DamageAmount = pending.DamageAmount,
                CritChance = pending.CritChance,
                CritMultiplier = pending.CritMultiplier,
                DirectDamageEnabled = pending.DirectDamageEnabled,
                SourceNodeId = pending.SourceNodeId,
                StackEffect = pending.StackEffect
            });
        }

        private void WriteSpawn(CombatPendingSpawn pending)
        {
            if (pending.Scope == Entity.Null || !Spawns.HasBuffer(pending.Scope))
            {
                return;
            }

            Spawns[pending.Scope].Add(new CombatSpawnElement
            {
                SourceId = pending.SourceId,
                TypeId = pending.TypeId,
                TargetId = pending.TargetId,
                Position = pending.Position,
                TargetPosition = pending.TargetPosition,
                Kind = pending.Kind,
                SourceNodeId = pending.SourceNodeId,
                ImpactAoe = pending.ImpactAoe,
                ImpactProjectile = pending.ImpactProjectile,
                ProjectileBurst = pending.ProjectileBurst
            });
        }
    }
}
