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
            int count = PendingHits.Count;
            if (count == 0) return;

            // Group into buckets single-threaded — no parallel writer contention here.
            var grouped = new NativeParallelMultiHashMap<CombatHitBucketKey, CombatPendingHit>(count, Allocator.Temp);
            while (PendingHits.TryDequeue(out CombatPendingHit pending))
                grouped.Add(new CombatHitBucketKey(pending.Scope, pending.TargetId), pending);

            NativeArray<CombatHitBucketKey> allKeys = grouped.GetKeyArray(Allocator.Temp);
            var seen = new NativeHashSet<CombatHitBucketKey>(allKeys.Length, Allocator.Temp);

            for (int k = 0; k < allKeys.Length; k++)
            {
                CombatHitBucketKey key = allKeys[k];
                if (!seen.Add(key)) continue;
                if (key.Scope == Entity.Null || !Hits.HasBuffer(key.Scope)) continue;

                if (!grouped.TryGetFirstValue(key, out CombatPendingHit pending, out var it)) continue;
                do { WriteHit(key.Scope, pending); }
                while (grouped.TryGetNextValue(out pending, ref it));
            }

            seen.Dispose();
            allKeys.Dispose();
            grouped.Dispose();
        }

        private void WriteHit(Entity scope, CombatPendingHit pending)
        {
            int payloadIndex = -1;
            if (pending.StackEffect.Enabled)
            {
                DynamicBuffer<CombatHitPayloadElement> payloadBuf = Payloads[scope];
                payloadIndex = payloadBuf.Length;
                payloadBuf.Add(new CombatHitPayloadElement
                {
                    StackEffect = pending.StackEffect
                });
            }

            int effectIndex = -1;
            if (pending.ImpactAoe.Enabled || pending.ImpactProjectile.Enabled || pending.ProjectileBurst.Enabled)
            {
                DynamicBuffer<CombatHitEffectElement> effectBuf = Effects[scope];
                effectIndex = effectBuf.Length;
                effectBuf.Add(new CombatHitEffectElement
                {
                    ImpactAoe = pending.ImpactAoe,
                    ImpactProjectile = pending.ImpactProjectile,
                    ProjectileBurst = pending.ProjectileBurst
                });
            }

            Hits[scope].Add(new CombatHitElement
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
