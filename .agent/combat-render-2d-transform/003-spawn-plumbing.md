# 003 — Spawn plumbing: carry + write the authoring component

## Scope
Registry, command structs, template builders, both archetypes, all spawn write
sites.

## Change

### Registry (`CombatRenderComponents.cs`)
`GetProjectileRenderComponent` / `GetAoeRenderComponent` currently return one
`CombatRenderComponent` built via `SetVisualTransform`. Change them to produce
**both**:
- render (compact): set `RenderMeta` (RenderTypeId + align bit: 1 for
  projectile, 0 for AOE), `Position.z = RenderZ`. `Rotation` may be left default
  (prepare fills it before the first draw; PresentationSystemGroup prepare runs
  ahead of batched render every frame including spawn frame).
- authoring: `BaseScale = entry.VisualScale` (projectile) or
  `geometry.VisualScale * entry.VisualScale` (AOE); `BaseSin/BaseCos` from entry.

Suggested surface: return via `out CombatRenderAuthoring` or a small
`(CombatRenderComponent, CombatRenderAuthoring)` pair. Add
`ProjectileTemplateAuthoring(renderId)` / `AoeTemplateAuthoring(renderId,
geometry)` helpers on `CombatRoot` mirroring the existing template accessors
(CombatRoot:274-278).

### Command structs
- `ProjectileSpawnPipeline.cs:55` and `AoeSpawnPipeline.cs:47`: add
  `public CombatRenderAuthoring Authoring;` next to `Render`.

### Template builders (`PlayerSkillDriver.cs`)
- `BuildProjectileTemplate` (~653) and `BuildAoeTemplate` (~692): set
  `Authoring = root != null ? root.ProjectileTemplateAuthoring(...) :
  <fallback>`.
- Fallbacks `ProjectileRenderComponentFor(prefab)` /
  `AoeRenderComponentFor(geometry)` (root == null path): split each into a
  render + authoring producer (read these methods; they build the base via the
  same scale/sin/cos the registry uses).

### RenderZ / template pass-through
- `ProjectileSpawnExpansionSystem.cs:203-219`: `render.RenderZ = ...` still works
  (RenderZ -> Position.z). `Authoring` rides along automatically via
  `var command = template` — no change needed beyond it existing on the command.
- `CombatRoot.cs:512-514` (`SpawnTemplateFor`): `render.RenderZ = 0f` still valid.

### Archetypes + write sites
Add `typeof(CombatRenderAuthoring)` to all three archetypes and set it wherever
`Render`/`CombatRenderComponent` is set today:
- `ProjectileSpawnApplySystem.cs`: archetype (40-55); `ProjectileSpawnJob` add
  `AuthoringHandle` + `authorings[i] = cfg.Authoring` (~382); ECB path
  `RecordCommonProjectileReset` `ecb.SetComponent(entity, cmd.Authoring)` (~206).
- `AoeSpawnApplySystem.cs`: both archetypes (impact 33-45, lingering 236-253);
  `ImpactAoeSpawnJob` + `LingeringAoeSpawnJob` add `AuthoringHandle`; extend
  `AoeSpawnApplyUtility.WriteCommon` to also write `authorings[index] =
  cfg.Authoring`; ECB paths `RecordImpactReset` (~473) and `RecordLingeringReset`
  (~501) `ecb.SetComponent(entity, cmd.Authoring)`.

## Acceptance
- All three archetypes contain `CombatRenderAuthoring`; every reuse and cold-
  create path seeds it from the command.
- Reuse still claims any disabled slot (no new chunk partitioning) — the added
  component is on every entity of the archetype, so pooling is unaffected.
- Spawned projectiles/AOEs render correct scale + rotation on their first frame.

## Depends on
001, 002.
