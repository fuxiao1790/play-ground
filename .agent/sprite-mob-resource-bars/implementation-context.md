# Implementation Context

## Architectural Decisions
- Replace UI Toolkit world-label mob bars with world-space sprite bars authored per-prefab.
- `MobResourceBarSprite` is a focused child MonoBehaviour: presentation only (renderers, fill scale, visibility, settings binding). No health/gameplay authority.
- `MobRoot` remains health source of truth; forwards `Resource.Changed` to the child presenter. No projection, no per-frame bar writes.
- Parent Transform hierarchy handles bar motion; no camera projection code.

## Global Invariants
- No `Update`/`LateUpdate`/camera access/scene search/material clone/allocation in presenter refresh methods.
- Fill scale writes only when the quantized step (36-step default, `Min(36)`) actually changes.
- Visibility = aliveVisible && settingsVisible, both composed before touching renderer.enabled.
- `GameSettings.DisplayMobHealthBars` remains sole persisted display-option owner; presenter subscribes in OnEnable, unsubscribes in OnDisable, and supports late binding (settings assigned after OnEnable already ran, since pooled/scene mobs enable before external binder code runs).
- Absent child bar = no presentation (must not throw). Authored bar with no settings binding defaults visible=true.
- Agent must NOT hand-edit `.prefab`, `.unity`, `.asset`, or Inspector-generated `.meta` files, and must not run editor automation to do it indirectly. That work is task 003, user-owned.

## Ownership Boundaries
- Scene/authoring owns Transforms + SpriteRenderer presentation (prefab work = task 003, user-owned).
- `MobRoot` owns serialized validation, runtime resource state, death, and coordinates the child presenter.
- ECS/simulation never touches SpriteRenderer/Transform/presenter references.

## Data Flow
ECS health -> `MobRoot.Resource` (`health` field) -> `Resource.Changed` event -> one handler in `MobRoot` computing `Current/Max` -> `MobResourceBarSprite.SetHealth(current, max)`.
Settings: `GameSettings` (existing singleton-per-scene component) -> `GameRoot` binds to static `mobs[]` -> `SpawnController` caches same instance, binds each rented mob in `WireMob` -> `MobRoot` delegates to child presenter's `BindGameSettings`.

## Lifecycle / Allocation Rules
- Cross-component subscriptions: subscribe in OnEnable, unsubscribe in OnDisable (existing coding standard).
- `MobPool.Rent` reactivates + calls `InitializeForSpawn`; `MobPool.Return` deactivates + reparents. Every rent must restore full/current fill + alive visibility; soft death must hide both renderers before pooled return. No allocation in any of these paths.

## ECS / Job / Threading Constraints
- N/A for tasks 001/002 beyond "no ECS job ever receives a SpriteRenderer/Transform/presenter reference." All work here is managed/main-thread MonoBehaviour code.

## Determinism Requirements
- None beyond existing (wander RNG untouched by this work).

## Producer / Consumer Separation
- `MobRoot` is producer (health authority); `MobResourceBarSprite` is pure consumer/presenter.

## Reused Mechanisms
- `MobRoot` as actor resource/lifecycle owner; `Resource.Changed` as sole health-change notification; `GameSettings.DisplayMobHealthBars` + its change event; `GameRoot`/`SpawnController` dependency binding; `MobPool` rent/return lifecycle.

## Introduced Mechanisms
- `MobResourceBarSprite` (new file, `Assets/Scripts/Mob/MobResourceBarSprite.cs`, namespace `PlayGround.Mob`, assembly `PlayGround.GameLogic`).
- 36-step fill quantization (`Min(36)`, default 36).

## Validation Requirements
- Tasks 001/002 validation = compile check (no Unity test runner invocation required by index unless user runs it); task 005 owns full test-suite work later. For 001/002 I will grep/read to confirm no leftover references and reason through correctness; user can compile in-editor.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Mob/MobRoot.cs` (existing, read) — fields: `resourceBarOffset` (Vector3, to remove), `ResourceBarAnchorPosition` (property, to remove), `health`/`mana` (`Resource`), `InitializeForSpawn`, `SoftDie`, `ReceiveCombatTick`, `MirrorResourcesFromProxy`.
- `Assets/Scripts/Common/Stats/Resource.cs` (existing, read) — `Changed`/`Depleted` events, `Current`/`Max`.
- `Assets/Scripts/Game/GameSettings.cs` (existing, read) — `DisplayMobHealthBars` bool + `DisplayMobHealthBarsChanged` event.
- `Assets/Scripts/Game/GameRoot.cs` (existing, read) — needs new `[SerializeField] private GameSettings gameSettings;`, bind to `mobs[]` in `Start`, pass into `spawnController.Bind(...)`.
- `Assets/Scripts/Spawn/SpawnController.cs` (existing, read) — `Bind(CombatRoot, Transform)` needs a third `GameSettings` param; cache it; `WireMob` binds it to each rented mob.
- `Assets/Scripts/Spawn/MobPool.cs` (existing, read) — `Rent`/`Return`, unaffected by 001/002 directly.
- `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs` + `.uxml`/`.uss` — old path, task 004 removes; task 001/002 do not touch.
- `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss` — visual reference only: dark bg `rgba(8,10,20,0.88)`, red fill `rgba(196,54,54,1)`, 36x5 aspect. Informs task 003 authoring, not code.
- Old prefab field `resourceBarOffset` values for reference only (not runtime): Bat `0.5`, Slime `0.8`, Skeleton `0.6`.
- Test call sites that must keep compiling: `Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs:214` calls `controller.Bind(combatRoot, null)` — must update for new 3-arg signature.
- `PlayGround.GameLogic.asmdef` covers `Assets/Scripts/Mob`, `Assets/Scripts/Game`, `Assets/Scripts/Spawn` (single top-level assembly; `Ui`/`Sim`/`Debugging` are separate asmdefs).
