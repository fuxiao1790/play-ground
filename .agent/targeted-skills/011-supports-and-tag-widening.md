# 011 — Supports and tag widening

**Depends on:** 009. **Scope:** small. **Risk:** `Any` widening is a behaviour change.

## Why

Without a multiplicity support, targeted skills sit outside the build lever both other domains
have. And several existing supports should apply to chains the moment the tag exists.

## Changes

`Assets/Scripts/Skills/SkillDefinitionTags.cs`

- Add `Targeted = 1 << 2`.
- Widen `Any` to `Projectile | Aoe | Targeted`.
- Update `SkillDefinitionTagUtility.Format` — it currently returns the literal
  `"projectile or AOE"` for `Any`.

**This is an intended behaviour change**, not incidental. The three current `Any` consumers start
accepting targeted skills:

| Consumer | Effect |
|---|---|
| `StackTrigger` | A chain becomes a stack applicator — free, no other work. |
| `AddedDamageSupport` | Adds flat damage to a chain. |
| `IncreasedRateSupport` | Increases chain cast rate. |

`Assets/Scripts/Skills/Support/IncreasedAoeSupport.cs`, `ConcentratedEffectSupport.cs`

- Add `Targeted` to `SupportedSkillTags`. Both already modify `AreaSize`, and task 009 folds the
  chain radii through that stat, so they scale a chain's reach with no further change
  (requirements decision 5).

`Assets/Scripts/Skills/Support/MultipleChainsSupport.cs` (new)

- Third member of the `MultipleProjectilesSupport` / `MultipleAoesSupport` family.
- Adds `count`, and modifies `ManaCost` through the same three `IManaModifiers` interfaces
  (added / increased / multiplier) both siblings implement.
- **Contributes no geometry field** — there is no scatter or spread to add (requirements
  decision 14). This is the one place it diverges from its siblings.
- `SupportedSkillTags => SkillDefinitionTags.Targeted`.
- Applies through a behaviour-modifier context mirroring `IAoeBehaviorModifier`; add
  `ITargetedBehaviorModifier` + `TargetedBehaviorContext` alongside the existing two if the compiler
  needs a typed context.

Projectile-only supports (`MultipleProjectilesSupport`, `PiercingSupport`, `HomingSupport`,
`FasterProjectilesSupport`) stay projectile-only and compile as no-ops with a tag-mismatch warning
on a targeted set — the existing behaviour, no change needed.

Chain **length** (`maxTargets`) still has no support; it is authored on the skill. That is the
deferred one, not multiplicity.

## Acceptance criteria

- EditMode: `SkillDefinitionTags.Any` includes `Targeted`, and `Format(Any)` no longer claims
  "projectile or AOE".
- EditMode: `AddedDamageSupport` on a targeted set raises compiled `Damage` (no tag warning).
- EditMode: `IncreasedRateSupport` on a targeted set lowers `RecoveryTime` (no tag warning).
- EditMode: `IncreasedAoeSupport` on a targeted set raises both `AcquireRadius` and `ChainRadius`.
- EditMode: `MultipleChainsSupport` with `count = 2` on a skill authoring `count = 1` compiles to
  `Count = 3`, and raises `ManaCost` per its three mana fields.
- EditMode: `MultipleProjectilesSupport` on a targeted set compiles as a no-op **and** returns a
  tag-mismatch warning.
- EditMode: `StackTrigger` with a targeted applicator produces no tag warning.
- All existing support tests pass — in particular, no projectile or AOE support changes behaviour
  because of the `Any` widening.

## Notes

Verify the `Any` widening against every `SupportedSkillTags` default: `SkillSupport` returns `Any`
virtually, so any support that does not override it now accepts targeted skills. Check each
override before assuming the three consumers above are the complete list.
