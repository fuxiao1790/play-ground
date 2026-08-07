# Task Execution Packet

## Task

006-render-and-link-vfx.md

## Goal

Add TargetedTag to existing batched render query; verify targeted render pose and already-emitted line/hit VFX contracts.

## Files Allowed To Modify

- `Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`
- Directly relevant EditMode render/VFX tests.

## Boundaries

- Only query widening; do not change VFX root/dispatcher or create targeted render system.
- Targeted render uses existing kinematics mirror, RenderTypeId zero behavior, IDs/sizes/timing split, and resolver VFX producer.

## Validation

- Static TargetedTag query and no VFX dispatcher changes; focused tests when editor available.
