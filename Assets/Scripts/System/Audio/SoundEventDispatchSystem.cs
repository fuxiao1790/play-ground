using PlayGround.System.Combat.Stats;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Audio
{
    // PresentationSystemGroup and MonoBehaviour LateUpdate share PreLateUpdate.
    // Their relative order must be confirmed in the Unity Profiler before changing
    // this push-based transport; a dispatch after LateUpdate intentionally lands in
    // AudioRoot's next frame batch until that measurement justifies a pull-based drain.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SoundEventDispatchSystem : SystemBase
    {
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = Entity.Null;
            var eventsByClip =
                new NativeParallelMultiHashMap<int, SoundEvent>(1, Allocator.Persistent);
            var clipIds = new NativeParallelHashSet<int>(1, Allocator.Persistent);
            try
            {
                singletonEntity = EntityManager.CreateEntity(typeof(SoundEventSingleton));
                EntityManager.SetComponentData(singletonEntity, new SoundEventSingleton
                {
                    EventsByClip = eventsByClip,
                    ClipIds = clipIds
                });
            }
            catch
            {
                if (eventsByClip.IsCreated)
                {
                    eventsByClip.Dispose();
                }

                if (clipIds.IsCreated)
                {
                    clipIds.Dispose();
                }

                throw;
            }
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<SoundEventSingleton>(singletonEntity))
            {
                return;
            }

            SoundEventSingleton lane =
                EntityManager.GetComponentData<SoundEventSingleton>(singletonEntity);
            lane.ProducerHandle.Complete();
            if (lane.EventsByClip.IsCreated)
            {
                lane.EventsByClip.Dispose();
            }

            if (lane.ClipIds.IsCreated)
            {
                lane.ClipIds.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!SystemAPI.TryGetSingletonRW<SoundEventSingleton>(
                    out RefRW<SoundEventSingleton> soundLane))
            {
                return;
            }

            ref SoundEventSingleton lane = ref soundLane.ValueRW;
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;
            if (!lane.EventsByClip.IsCreated || !lane.ClipIds.IsCreated)
            {
                return;
            }

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(
                    out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.SoundEventsCreated += lane.EventsByClip.Count();
            }

            AudioRoot root = AudioRoot.Instance;
            if (root == null)
            {
                lane.EventsByClip.Clear();
                lane.ClipIds.Clear();
                return;
            }

            foreach (int clipId in lane.ClipIds)
            {
                if (!lane.EventsByClip.TryGetFirstValue(
                        clipId,
                        out SoundEvent soundEvent,
                        out NativeParallelMultiHashMapIterator<int> iterator))
                {
                    continue;
                }

                do
                {
                    root.EnqueueBucketed(clipId, in soundEvent);
                }
                while (lane.EventsByClip.TryGetNextValue(out soundEvent, ref iterator));
            }

            lane.EventsByClip.Clear();
            lane.ClipIds.Clear();
        }
    }
}
