# 002 — Area-Timed payload struct + second queue on the singleton

## Goal
Carry the new contract's data from producers to the drain, reusing the existing lane singleton.

## Changes
- **`Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`**
  - Add payload with an `// ECS Lifecycle:` comment (transient native payload; not on entities;
    queued into the singleton's `PendingAreaTimed`):
    ```csharp
    public struct VfxAreaTimedRequest
    {
        public int TypeId;
        public AoeVfxTrigger Trigger;
        public float2 Position;
        public float AreaSize;
        public float DurationMs;
        public float TickIntervalMs;
    }
    ```
- **`Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`**
  - `CombatAoeVfxDispatchSingleton`: add `public NativeQueue<VfxAreaTimedRequest> PendingAreaTimed;`
    with an `// ECS Lifecycle:` comment. Keep the single shared `ProducerHandle` (covers producers
    to both queues).
  - `OnCreate`: allocate `PendingAreaTimed` (`Allocator.Persistent`).
  - `OnDestroy`: after `ProducerHandle.Complete()`, dispose `PendingAreaTimed` if created (alongside
    the existing `PendingAoeSpawns`).
  - `OnUpdate`: after completing/clearing `ProducerHandle`, early-return only when **both**
    `PendingAoeSpawns.Count == 0 && PendingAreaTimed.Count == 0`. When root is null, clear **both**
    queues. Pass the new queue to the root drain (signature updated in task 003).

## Notes / constraints
- One `ProducerHandle` still suffices: producers writing either queue combine into it; the system
  completes it once before draining both. Matches `Docs/coding-standards.md:213-216`.
- Do not add a second `ProducerHandle` — nothing needs independent completion.

## Acceptance criteria
- Singleton owns both queues; both created in `OnCreate`, disposed in `OnDestroy`.
- `OnUpdate` gate and null-root clear consider both queues.
- No per-frame allocation; drain still `while (TryDequeue)` (in task 003).

## Dependencies
- Struct references `AoeVfxTrigger` (existing). Independent of 001, but lands together.
