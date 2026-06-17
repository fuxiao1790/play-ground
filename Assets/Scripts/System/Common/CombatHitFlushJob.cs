using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Common
{
    // Flushes the per-lane damage stream into scope-owned CombatDamageElement buffers.
    [BurstCompile]
    public struct CombatHitFlushJob : IJob
    {
        public Entity Scope;
        public NativeStream PendingDamage;
        public BufferLookup<CombatDamageElement> Damage;

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
        }

        private void WriteDamage(CombatPendingDamage pending)
        {
            if (pending.Faction == CombatFaction.None || !Damage.HasBuffer(Scope))
            {
                return;
            }

            Damage[Scope].Add(new CombatDamageElement
            {
                Faction = pending.Faction,
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
    }
}
