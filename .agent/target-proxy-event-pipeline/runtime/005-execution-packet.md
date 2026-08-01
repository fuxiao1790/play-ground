# Task Execution Packet

## Task

005-skilldriver-lazy-caster.md

## Goal

Replace one-time caster entity snapshot with live owner lookup.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDriver.cs`

## Required Changes

- Store `ICombatTarget casterOwner`, bind owner, and supply `casterOwner?.CombatTargetProxy ?? Entity.Null` at spawn translation.

## Scope Constraint

- Do not change PlayerRoot/MobRoot yet; task 006 owns call sites.

## Validation

- Static signature/use scan and diff check.
