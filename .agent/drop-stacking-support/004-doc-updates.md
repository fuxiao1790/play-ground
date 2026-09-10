# 004 — Doc Updates

**Scope:** small-medium. Three files; one of them in several places.
**Depends on:** 001.

## `Docs/reference/game-logic/skill-system.md`

The doc has also drifted from the code independently of this change — it
documents `debuffName` and `cosmeticDebuffStatus` as authored `StackingSupport`
fields. Neither exists: `DebuffName` is compiler-derived from the skill asset
name and `cosmeticDebuffStatus` was never implemented. Fix that drift while
rewriting rather than carrying it forward.

| Section | Change |
|---|---|
| **Concepts** — "Stacking Support" entry | Delete. Fold what remains into the `StackTrigger` description: a stack detonation is any skill set reached through a `StackTrigger`, and the trigger owns the accrual config. |
| **`StackingSupport`** section | Retitle to "Stack Detonations". Field list becomes `stackThreshold`, `debuffLifetimeSeconds`, `stacksPerHit` **on `StackTrigger`**. Drop `debuffName` / `cosmeticDebuffStatus` from the authored list and state that `DebuffName` is derived from the effect set's skill name for display/debug only. Keep the existing paragraphs on debuff-key minting, `HitApplyFinalizeSystem` accrual, `StatusProcessSystem` detonation, and projectile-nova detonation — all unchanged. |
| **Supports** section | Delete the `ConversionSupport` / `StackingSupport` sentences. There is no conversion-support concept left. |
| **`StackTrigger`** trigger entry | Show the new field block. Compatible tags become: source `Projectile`, `Aoe`, or `Targeted`; **target `Projectile` or `Aoe`** (a targeted detonation bakes no snapshot). Delete "target set must have `StackingSupport`". |
| **Validation Warnings** list | Remove the three stacking bullets ("stacking set is not the effect of a `StackTrigger`", "`StackTrigger` targets a set without `StackingSupport`", "stacking set is targeted by a normal trigger link"). |
| **Creating a Stacking Detonation** (authoring guide) | Rewrite: create the detonation skill and its set normally; create a `StackTrigger` and set threshold/lifetime/stacksPerHit on it; wire applicator → trigger → detonation set. The current step 2-3 (create support, add to set) disappears. |
| **Legacy compilation pseudocode** | The `StackTrigger` line already reads "compile chain.effect recursively to `RuntimeStackingDetonation`"; adjust to show the trigger constructing the wrapper. Delete the `ConversionSupport` line from the compile loop. These sections are marked Historical — a light touch is enough, or mark the stacking paragraphs superseded. |
| **Player-facing type table** | Remove the `StackingSupport` row. |
| **Examples** — stacking chain | Drop `+ StackingSupport` from the example set names. |
| **Set Isolation Rules** | Worth an added sentence: whether a set compiles as a detonation is decided by the link that reaches it, not by the set — which is why the same set can be a normal root in one position and a detonation in another. |

## `Docs/reference/game-logic/skill-modifiers.md`

`:23-26` — delete the `ConversionSupport` / `StackingSupport` sentences; the
support roster is now stat-modifier supports only.

## `Docs/reference/simulation/spawn-template-registry.md`

`:477-485` — "Current stacking direction" step 1 reads "A normal skill set with
`StackingSupport` compiles to `RuntimeStackingDetonation`". Change to: a
`StackTrigger` compiles its effect set and wraps it in `RuntimeStackingDetonation`.
The rest of that section (key minting, `StackEffectSnapshot` copying without
transformation) is accurate and stays.

## Not affected

- `Docs/contracts/sound-events.md:20` and
  `Docs/reference/architecture/game-logic-simulation-boundary.md:38` mention
  "stacking detonations" generically with no reference to the support. Leave them.

## Acceptance criteria

- [ ] `grep -ri "stackingsupport\|conversionsupport" Docs/` returns nothing outside
      sections explicitly marked Historical.
- [ ] No doc still lists `debuffName` or `cosmeticDebuffStatus` as authored fields.
- [ ] The authoring guide can be followed start to finish against the post-001
      code without encountering a type that no longer exists.
