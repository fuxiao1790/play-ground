using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Common
{
    // Flushes the per-lane damage stream into scope-owned CombatDamageElement
    // buffers. Internal follow-up spawns no longer pass through here — they are
    // converted to ECS spawn requests by CombatSpawnConvertJob.
    [BurstCompile]
    public struct CombatHitFlushJob : IJob
    {
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
    }
}
