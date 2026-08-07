using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Spawning;
using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    internal static class TargetedSpawnEmission
    {
        private const int IdSalt = 0x4D7A31;

        public static void Enqueue(
            int sourceId,
            int typeId,
            CombatFaction faction,
            float2 impactPosition,
            int targetKey,
            IntervalChildKind kind,
            Unity.Entities.Hash128 templateKey,
            NativeQueue<TargetedSpawnEvent>.ParallelWriter targetedWriter)
        {
            if (kind != IntervalChildKind.Targeted)
                return;

            int id = HashId(sourceId, typeId, targetKey, IdSalt);
            uint jitterSeed = (uint)id * 2654435761u;
            targetedWriter.Enqueue(new TargetedSpawnEvent
            {
                Kind = kind,
                TemplateKey = templateKey,
                Faction = faction,
                Position = impactPosition,
                AcquireAnchor = impactPosition,
                SourceId = id,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = 0,
                ContactGateSeedTargetId = targetKey
            });
        }

        private static int HashId(int a, int b, int c, int salt)
        {
            unchecked
            {
                int hash = salt;
                hash = (hash * 397) ^ a;
                hash = (hash * 397) ^ b;
                hash = (hash * 397) ^ c;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }
    }
}
