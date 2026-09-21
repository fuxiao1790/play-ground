using PlayGround.System.Combat.Core;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    // ECS Lifecycle: transient proxy-creation intent; appended to the shared combat scope
    // by the managed combat bridge, consumed and discarded by TargetProxyCreateApplySystem.
    public struct TargetProxyCreateEvent : IBufferElementData
    {
        public int Token;
        public TargetFaction FactionPolicy;
        public TargetPosition Position;
        public TargetCollisionShape Shape;
        public float MaxHealth;
        public float CurrentHealth;
        public float HealthRegenPerSecond;
        public float MaxMana;
        public float CurrentMana;
        public float ManaRegenPerSecond;
    }

    public enum TargetProxyUpdateKind : byte
    {
        Push,
        PushResourceMaxes,
        SetHealth,
        SetMana
    }

    // ECS Lifecycle: transient proxy-update intent; appended to the shared combat scope
    // by the managed combat bridge, consumed and discarded by TargetProxyUpdateApplySystem.
    public struct TargetProxyUpdateEvent : IBufferElementData
    {
        public TargetProxyUpdateKind Kind;
        public Entity Proxy;
        public TargetPosition Position;
        public TargetCollisionShape Shape;
        public float MaxHealth;
        public float HealthRegenPerSecond;
        public float MaxMana;
        public float ManaRegenPerSecond;
        public float CurrentValue;
    }

    // ECS Lifecycle: transient proxy-deletion intent; appended to the shared combat scope
    // by the managed combat bridge, consumed and discarded by TargetProxyDeleteApplySystem.
    public struct TargetProxyDeleteEvent : IBufferElementData
    {
        public Entity Proxy;
    }
}
