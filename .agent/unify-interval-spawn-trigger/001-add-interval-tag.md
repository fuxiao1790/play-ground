# 001 — Add an `Interval` skill tag and use it for source eligibility

## Why

Today, `SkillDefinitionTags` has `Projectile`, `Aoe`, `Targeted` — one flag per
definition *shape*. There is no flag for "has a duration to accrue energy
over," which is the actual property an interval-spawn source needs. As a
result, pulse `AoeSkill` and duration `LingeringAoeSkill` are
tag-indistinguishable (`AoeSkillBase.Tags` is `sealed override => Aoe` for
both), and the current pulse-AOE rejection lives as a separate, ad-hoc
`Definition`-type check (`SkillLoadoutValidator.ValidateIntervalSpawnSource`:
`causeSkill.Definition is AoeDefinitionBase and not LingeringAoeDefinition`)
instead of the same generic tag-mismatch machinery every other trigger uses —
and it's Warning severity, not Error, so an invalid source doesn't validate as
a hard failure the way it should.

This task adds a real `Interval` tag so eligibility becomes a tag fact like
everything else, which lets [004](./004-update-validator.md) delete the
ad-hoc check entirely and fold it into the generic path with Error severity.

## Scope

- `Assets/Scripts/Skills/SkillDefinitionTags.cs`
- `Assets/Scripts/Skills/Skill/ProjectileSkill.cs`
- `Assets/Scripts/Skills/Skill/AoeSkill.cs`
- `Assets/Scripts/Skills/Skill/LingeringAoeSkill.cs`
- `Assets/Scripts/Skills/Skill/TargetedSkill.cs` (unchanged — see below)

## Change

### `SkillDefinitionTags.cs`

Add a fourth flag, kept out of `Any` since it's an orthogonal capability, not
a definition shape:

```csharp
[Flags]
public enum SkillDefinitionTags
{
    None = 0,
    Projectile = 1 << 0,
    Aoe = 1 << 1,
    Targeted = 1 << 2,
    Interval = 1 << 3,
    Any = Projectile | Aoe | Targeted,
}
```

`Any` stays `Projectile | Aoe | Targeted` — it means "any definition shape,"
and every existing `SupportedSkillTags => Any` (on `SkillSupport` and its
subclasses) should keep matching every skill regardless of whether that skill
also happens to carry `Interval`. `HasAny` is bitwise, so this is automatic;
no support-side code needs to change.

### `ProjectileSkill.cs`

Every projectile has a lifetime to accrue energy over, so every
`ProjectileSkill` qualifies:

```csharp
public override SkillDefinitionTags Tags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Interval;
```

### `AoeSkill.cs` / `LingeringAoeSkill.cs`

`AoeSkillBase.Tags` is currently `sealed`, which is exactly what forced the
ad-hoc `Definition`-type check to exist — a sealed override can't be
specialized further by `LingeringAoeSkill`. Un-seal it and let
`LingeringAoeSkill` add `Interval`:

```csharp
// AoeSkill.cs
public abstract class AoeSkillBase : Skill
{
    public override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe;
}

// AoeSkill (pulse) keeps inheriting Aoe with no Interval — no change needed
// to the AoeSkill class body itself, it doesn't declare Tags today and still
// doesn't need to.
```

```csharp
// LingeringAoeSkill.cs
public sealed class LingeringAoeSkill : AoeSkillBase
{
    [SerializeField] private LingeringAoeDefinition definition = new();

    public override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe | SkillDefinitionTags.Interval;
    public override SkillDefinition Definition => definition;
}
```

### `TargetedSkill.cs` — unchanged

`TargetedSkillBase.Tags` stays `sealed override => Targeted`, no `Interval`.
Per existing docs: "A targeted chain is never an interval source: it has no
duration of its own to accrue energy over." This is already correctly
excluded and needs no change.

## Acceptance Criteria

- `ProjectileSkill.Tags` includes both `Projectile` and `Interval`.
- `LingeringAoeSkill.Tags` includes both `Aoe` and `Interval`.
- `AoeSkill.Tags` (pulse) is still exactly `Aoe`, no `Interval`.
- `TargetedSkill.Tags` is still exactly `Targeted`, no `Interval`.
- `SkillDefinitionTags.Any` is unchanged (`Projectile | Aoe | Targeted`) and
  does not include `Interval`.
- No existing `SupportedSkillTags => Any` support becomes newly
  incompatible with any skill — verify by inspection that every
  `HasAny(skillTags, Any)` call site still passes regardless of the new bit
  (bitwise `HasAny` guarantees this, but worth a sanity check against
  `SkillLoadoutUi.cs:291`'s support-picker filter, the one place `Tags` feeds
  a compatibility check today).

## Dependencies

None — first task. Blocks [002](./002-merge-trigger-class.md) (which sets
`IntervalSpawnTrigger.SourceSkillTags = Interval`) and
[004](./004-update-validator.md).

## Scope/Complexity

Small. Four small, mechanical edits across the `Skill` hierarchy plus the
enum.
