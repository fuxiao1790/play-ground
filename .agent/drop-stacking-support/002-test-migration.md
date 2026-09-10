# 002 — Migrate Tests Off StackingSupport

**Scope:** medium. Two test files, ~12 tests.
**Depends on:** 001 — and must land in the **same commit**: the EditMode assembly
references `StackingSupport` directly, so a split leaves the test assembly
uncompilable.

## Shared fixture change

Everywhere a detonation set was built as `CreateSkillSet(name, skill, stackingSupport)`,
it becomes a plain `CreateSkillSet(name, skill)`, and the accrual values move to
the `StackTrigger` asset that reaches it:

```csharp
StackTrigger trigger = CreateAsset<StackTrigger>("Stack Trigger");
trigger.stackThreshold = 4;
```

## `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

| Test (line) | Action |
|---|---|
| `CompilerLeavesManaCostUnchangedForSupportsWithoutManaModifiers` (`:219`) | Used `StackingSupport` only as "a support with no mana modifiers". Swap in any plain stat support and drop the `RuntimeStackingDetonation` type assertions (`:231-233`) — the root now compiles to `RuntimeAoeDefinition` and `ManaCost` is read directly. |
| `CompilerBuildsStackingSupportRuntimeDetonation` (`:603`) | Rename to `CompilerBuildsStackTriggerRuntimeDetonation`. Build `[applicator] -StackTrigger(threshold 4)-> [detonation set]`, assert the applicator's `StackingDetonation` carries `StackThreshold == 4` and `DebuffName == detonationSkill.name`. |
| Field-absence guards (`:625-626`) | Keep the intent, retarget the type: assert `typeof(StackTrigger)` has no `debuffName` / `cosmeticDebuffStatus` field. These guard the "derive, don't author" decision and are still worth having. |
| `CompilerAppliesAdditiveSupportsToStackingSupportDetonation` (`:630`) | Rename to `...ToStackTriggerDetonation`. Same assertion target: stat supports on the detonation set still fold into the inner definition. This is the regression guard that the effect node's own supports survive the wrapping move. |
| `CompilerAttachesStackTriggerToProjectileApplicator` (`:712`), `...AfterProjectileIntervalSpawnLink` (`:735`), `...AfterNormalImpactLinks` (`:762`), `...ToStackingDetonationApplicator` (`:801`) | Mechanically drop the support from set construction; assertions on `StackingDetonation` / `.Detonation` stay valid. `:801` (detonation → `OnAoeHitSpawn`-style chaining onto a second stack trigger) is the direct test of invariant I3 — verify it still passes without the `triggerHost` unwrap. |
| `DriverDoesNotBindStackingSupportSetAsRoot` (`:836`) | **Premise inverts** (intended change #1). Replace with `DriverDoesNotBindStackTriggerEffectAsRoot`: a two-node loadout `[applicator] -StackTrigger-> [detonation]` binds exactly one root slot. Delete the "unwired detonation set is not a root" expectation — it is now a root by design. |
| `ValidatorWarnsWhenStackingSupportSetIsNotStackTriggerEffect` (`:890`) | Delete — the rule no longer exists. |
| `ValidatorWarnsWhenStackTriggerTargetHasNoStackingSupport` (`:904`) | Replace with `ValidatorWarnsWhenStackTriggerTargetsTargetedSkill`, asserting `UnsupportedTriggerTarget` from the generic tag path (I6). |
| `ValidatorWarnsWhenStackingSupportSetIsTargetedByNonStackTrigger` (`:922`) | Delete — a set is only a detonation by virtue of the link reaching it, so a normal link targeting it is now a valid ordinary chain. |

## `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`

`:143-144` — drop the `CreateAsset<StackingSupport>()` argument from the stack
child set; leave the `StackTrigger` node as is.

## `Assets/Tests/PlayMode/AoePlayModeTests.cs`

`ProjectileImpactAoeApplicatorStackTriggerDetonatesStackSet` (`:700`) — the
end-to-end accrual/detonation test. Delete the `stackingSupport` instance
(`:709`) and move `SetField(stackingSupport, "stackThreshold", 2)` (`:723`) to
`stackTrigger.stackThreshold = 2`. This test is the primary proof that the
`StackEffectSnapshot` still reaches ECS with the right threshold (I4/I6), so it
must pass unchanged in behavior.

## Not affected

`AoeSimulationTests`, `ProjectileCollisionSimulationTests`,
`TargetedSkillPlayModeTests`, and `CombatApplyFinalizeSingleSystem` tests
construct `RuntimeStackingDetonation` / `StackEffectSnapshot` directly. The
runtime shape is unchanged, so they need no edits — confirm by grep, don't touch.

## Acceptance criteria

- [ ] EditMode and PlayMode assemblies compile with no `StackingSupport` reference.
- [ ] All previously passing stacking tests pass with equivalent assertions.
- [ ] `ProjectileImpactAoeApplicatorStackTriggerDetonatesStackSet` still detonates
      at the same hit count as before the refactor.
- [ ] Exactly two validator tests removed with no replacement (the two rules that
      no longer have meaning), one replaced by the target-tag test.
