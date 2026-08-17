using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    // ECS Lifecycle: transient spawn intent; enqueued by producers into the expansion queue
    // or appended to the scope submission buffer; consumed and discarded by TargetedSpawnExpansionSystem.
    // HasAcquiredTarget is stamped only for root-cast gate intent and is discarded after expansion.
    public struct TargetedSpawnEvent : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Hash128 TemplateKey;
        public float2 Position;
        public float2 AcquireAnchor;
        public byte HasAcquiredTarget;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public int ContactGateSeedTargetId;
    }

    // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by
    // apply. Registry templates use this same shape with per-instance fields left default.
    // HasAcquiredTarget is per-instance expansion data and is cleared in normalized templates.
    public struct TargetedSpawnCommand
    {
        public CombatFaction Faction;
        public int TargetedId;
        public int TypeId;
        public int RenderTypeId;
        public SkillSoundIds SoundIds;
        public float SpawnSoundRadius;
        public int InstanceIndex;
        // Per-instance stamping frame, mirroring AoeSpawnCommand. Expansion copies these from the
        // event before fanning, and TargetedIdFor reads them back to derive unique per-fork ids.
        // Registry templates leave both default.
        public uint JitterSeed;
        public int DeterministicIdTickIndex;
        public float2 Origin;
        public float2 AcquireAnchor;
        public byte HasAcquiredTarget;
        public int EchoCount;
        // Fail-safe only. A chain expires the instant its walk ends, so this backstop is computed
        // from the walk's own worst-case duration and never authored.
        public float LifetimeSeconds;
        public float ArmSeconds;
        public CombatHitPayload HitPayload;
        public TargetedResolveConfig Resolve;
        public TargetedVfxIds VfxIds;
        public TargetedVfxSizeComponent VfxSize;
        public CombatRenderComponent Render;
        public CombatRenderAuthoring Authoring;
        public OnHitSpawnRef OnHitSpawn;
    }

    // ECS Lifecycle: stateless VFX timing mapper; reads spawn commands during targeted
    // materialization without retaining ECS state.
    public static class TargetedVfxUtility
    {
        public static VfxTimingData TimingFor(in TargetedSpawnCommand command) =>
            new()
            {
                Duration = command.LifetimeSeconds,
                TickInterval = 0f
            };
    }
}
