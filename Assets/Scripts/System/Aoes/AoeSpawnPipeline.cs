using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Aoes
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue
    // or appended to the scope submission buffer; consumed and discarded by ImpactAoeSpawnExpansionSystem.
    public struct ImpactAoeSpawnEvent : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Unity.Entities.Hash128 TemplateKey;
        public float2 Position;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public int ContactGateSeedTargetId;
    }

    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue
    // or appended to the scope submission buffer; consumed and discarded by LingeringAoeSpawnExpansionSystem.
    public struct LingeringAoeSpawnEvent : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Unity.Entities.Hash128 TemplateKey;
        public float2 Position;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public int ContactGateSeedTargetId;
    }

    public static class AoeVariant
    {
        public static IntervalChildKind AoeChildKindFor(float lifetimeSeconds) =>
            lifetimeSeconds > 0f ? IntervalChildKind.LingeringAoe : IntervalChildKind.ImpactAoe;

        public static StackDetonationKind AoeDetonationKindFor(float lifetimeSeconds) =>
            lifetimeSeconds > 0f ? StackDetonationKind.LingeringAoe : StackDetonationKind.ImpactAoe;
    }

    // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by apply.
    // Registry templates also use this command shape with per-instance fields default and volley fields populated.
    public struct AoeSpawnCommand
    {
        public CombatFaction Faction;
        public int AoeId;
        public int TypeId;
        public AoeVfxIds VfxIds;
        public SkillSoundIds SoundIds;
        public float SpawnSoundRadius;
        // Render identity, allocated from the unified render-resource id space (decoupled
        // from the behavior TypeId). Drives the CombatRenderKindId / registry lookup.
        public int RenderTypeId;
        public float Lifetime;
        public float ArmSeconds;
        public float RepeatHitCooldownSeconds;
        public CombatHitPayload HitPayload;
        public float AreaSize;
        public float Radius;
        public float RotationRadians;
        public float2 Position;
        public float2 HalfExtents;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public CombatShapeType ShapeType;
        public int EchoCount;
        public float ScatterRadius;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public CombatRenderComponent Render;
        public CombatRenderAuthoring Authoring;
        public OnHitSpawnRef OnHitSpawn;
        public int HasTimedSpawner;
        public TimedSpawnComponent TimedSpawn;
    }

}
