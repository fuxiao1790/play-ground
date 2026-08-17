using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Audio
{
    // ECS Lifecycle: singleton sound event lane; created and disposed by
    // SoundEventDispatchSystem, written during simulation by spawn expansion jobs,
    // drained and cleared by SoundEventDispatchSystem during presentation.
    public struct SoundEventSingleton : IComponentData
    {
        public NativeQueue<SoundEvent> Events;
        public JobHandle ProducerHandle;
    }
}
