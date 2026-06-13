using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Common
{
    [BurstCompile]
    public struct CombatHitFlushJob : IJob
    {
        public NativeQueue<CombatPendingHit> PendingHits;
        public BufferLookup<CombatHitElement> Hits;
        public BufferLookup<CombatHitPayloadElement> Payloads;
        public BufferLookup<CombatHitEffectElement> Effects;

        public void Execute()
        {
            while (PendingHits.TryDequeue(out CombatPendingHit pending))
            {
                if (pending.Scope == Entity.Null || !Hits.HasBuffer(pending.Scope))
                {
                    continue;
                }

                int payloadIndex = -1;
                if (pending.StackEffect.Enabled)
                {
                    DynamicBuffer<CombatHitPayloadElement> payloadBuf = Payloads[pending.Scope];
                    payloadIndex = payloadBuf.Length;
                    payloadBuf.Add(new CombatHitPayloadElement
                    {
                        StackEffect = pending.StackEffect
                    });
                }

                int effectIndex = -1;
                if (pending.ImpactAoe.Enabled || pending.ImpactProjectile.Enabled || pending.ProjectileBurst.Enabled)
                {
                    DynamicBuffer<CombatHitEffectElement> effectBuf = Effects[pending.Scope];
                    effectIndex = effectBuf.Length;
                    effectBuf.Add(new CombatHitEffectElement
                    {
                        ImpactAoe = pending.ImpactAoe,
                        ImpactProjectile = pending.ImpactProjectile,
                        ProjectileBurst = pending.ProjectileBurst
                    });
                }

                Hits[pending.Scope].Add(new CombatHitElement
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
                    Order = pending.Order,
                    PayloadIndex = payloadIndex,
                    EffectIndex = effectIndex
                });
            }
        }
    }
}
