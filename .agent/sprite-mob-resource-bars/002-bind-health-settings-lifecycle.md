# 002 — Bind Health, Settings, And Pooled Lifecycle

## Change

Refactor `MobRoot` to own one optional serialized
`MobResourceBarSprite resourceBar` child reference.

- Remove `resourceBarOffset` and `ResourceBarAnchorPosition`.
- Subscribe once to owned health `Resource.Changed`; route all visual refreshes
  through one handler calculating `Current / Max`.
- Initialize new/reused mobs with alive visibility before health reset, then
  refresh current health.
- Hide bar during `SoftDie`.
- Add a settings-binding method that delegates to child presenter when present.
- Keep an absent child supported for test-only/dedicated mobs; production prefab
  completeness is enforced by authoring tests rather than hot-path null errors.
- Do not add any bar work to `MobRoot.Update` beyond the existing health mirror;
  unchanged `Resource.MirrorCurrent` calls emit no `Changed` event.

Propagate existing settings without scene searches:

- add a plain, non-serialized `GameSettings gameSettings` field to `GameRoot`,
  resolved once via `GetComponent<GameSettings>()` in `Awake` — not a
  `[SerializeField]`/Inspector reference, matching the existing
  `MobRoot.skillDriver` precedent, since `GameSettings` always lives on the
  same GameObject as `GameRoot` and there is no legitimate case for pointing
  it elsewhere (went through a manually-wired version, then a
  `[SerializeField]`-with-fallback version, before landing here — see
  index.md for why each intermediate step still had unnecessary Inspector
  surface area);
- bind it to scene mobs during `GameRoot.Start`;
- extend `SpawnController.Bind` to receive/cache the same settings instance;
- bind settings to each rented mob in `SpawnController.WireMob`;
- update callers and fixtures for the bind signature;
- stripped test worlds may pass null, causing authored presenter default-visible
  behavior.

This task adds serialized fields/APIs in C# only. Assigning those fields on
prefabs or scene objects belongs to task 003 and must be done by the user in
Unity Inspector.

## Acceptance Criteria

- One health notification path drives fill for direct result replay, proxy
  mirroring, max-health authoring changes, reset, and regeneration.
- Moving a healthy mob without changing health invokes no presenter refresh and
  writes no bar local transform from script.
- Pooled mob with prior damage returns at correct reset health/fill and visible
  alive state.
- Soft-dead mob hides background and fill before pool return.
- Existing display setting toggles active bars without any per-frame scan.
- `GameSettings` remains sole persisted display-option owner.
- ECS code and contracts gain no SpriteRenderer/presenter reference.

## Dependencies

- 001 — child presenter API must exist.

## Scope / Complexity

Medium: `MobRoot`, `GameRoot`, `SpawnController`, and direct code callers. No
prefab or scene file edits.
