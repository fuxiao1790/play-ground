using System.Collections.Generic;
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
        private readonly List<SoundEvent> drainScratch = new();
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = Entity.Null;
            var events = new NativeQueue<SoundEvent>(Allocator.Persistent);
            try
            {
                singletonEntity = EntityManager.CreateEntity(typeof(SoundEventSingleton));
                EntityManager.SetComponentData(singletonEntity, new SoundEventSingleton
                {
                    Events = events
                });
            }
            catch
            {
                if (events.IsCreated)
                {
                    events.Dispose();
                }

                throw;
            }
        }

        protected override void OnDestroy()
        {
            drainScratch.Clear();
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<SoundEventSingleton>(singletonEntity))
            {
                return;
            }

            SoundEventSingleton lane =
                EntityManager.GetComponentData<SoundEventSingleton>(singletonEntity);
            lane.ProducerHandle.Complete();
            if (lane.Events.IsCreated)
            {
                lane.Events.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!SystemAPI.TryGetSingletonRW<SoundEventSingleton>(
                    out RefRW<SoundEventSingleton> soundLane))
            {
                drainScratch.Clear();
                return;
            }

            ref SoundEventSingleton lane = ref soundLane.ValueRW;
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;
            drainScratch.Clear();

            if (!lane.Events.IsCreated)
            {
                return;
            }

            AudioRoot root = AudioRoot.Instance;
            if (root == null)
            {
                lane.Events.Clear();
                return;
            }

            while (lane.Events.TryDequeue(out SoundEvent soundEvent))
            {
                drainScratch.Add(soundEvent);
            }

            for (int i = 0; i < drainScratch.Count; i++)
            {
                SoundEvent soundEvent = drainScratch[i];
                root.Enqueue(in soundEvent);
            }

            drainScratch.Clear();
        }
    }
}
