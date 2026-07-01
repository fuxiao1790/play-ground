# 003 — Echo Support + Template Builder Wiring

Add the authoring support (the AOE analog of `MultipleProjectilesSupport`) and
carry `echoCount`/`scatterRadius` from the runtime def into the command template.

## Changes

### New `Assets/Scripts/Skills/Support/MultipleAoesSupport.cs`
Mirror `MultipleProjectilesSupport` exactly, but for AOE:
```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple AOEs", fileName = "MultipleAoesSupport")]
public sealed class MultipleAoesSupport : StatModifierSupport, IAoeBehaviorModifier
{
    [SerializeField, Min(1)] private int echoCount = 3;
    [SerializeField, Min(0f)] private float scatterRadius = 2f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

    public void ApplyToAoe(AoeBehaviorContext ctx)
    {
        ctx.EchoCount = echoCount;
        ctx.ScatterRadius = scatterRadius;
    }
}
```
- Tag `Aoe` means placing it on a projectile compiles as a no-op and emits the
  existing tag-mismatch validation warning (no validator change needed — the
  generic `SupportedSkillTags` check already covers it).

### `PlayerSkillDriver.cs` — `SkillIntervalTemplateBuilder.BuildAoeTemplate` (~line 654)
- Rename the `int count` parameter to `int echoCount`.
- Set `EchoCount = Mathf.Max(1, echoCount),` (was `Count = …`).
- Set `ScatterRadius = child.ScatterRadius,`.
- Update both call sites in `PlayerSkillDriver`:
  - top-level AOE registration (~line 306): pass `Mathf.Max(1, aoeDef.EchoCount)`.
  - interval AOE registration (~line 399): pass `Mathf.Max(1, setup.Count)` (the
    interval burst count) — unchanged argument, new parameter name.

### `SkillSpawnTranslator.cs` — AOE branch (~line 46)
- `combatRoot.SpawnRegisteredAoe(aoe.SpawnTemplateKey, aimWorldPos, Mathf.Max(1, aoe.EchoCount), faction);`
  (rename `aoe.Count` → `aoe.EchoCount`). `SpawnRegisteredAoe`'s own `count`
  parameter (id reservation) is fine to leave named `count`.

## Acceptance criteria
- `MultipleAoesSupport` sets echo count + scatter through `AoeBehaviorContext`.
- `BuildAoeTemplate` stamps `EchoCount` + `ScatterRadius` onto the command
  template, so the content hash distinguishes echo/scatter variants.
- Top-level and interval AOE registration compile against the renamed builder.
- Placing the support on an AOE set produces scattered echoes; placing it on a
  projectile set warns and no-ops.

## Dependencies
- Depends on 002 (`RuntimeAoeDefinition.EchoCount/ScatterRadius`, context setters).
- Compiles with 001 (`AoeSpawnCommand.EchoCount/ScatterRadius`).

## Scope
Small: one new file + three edit sites.
