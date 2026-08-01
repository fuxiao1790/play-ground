# Task Execution Packet

## Task

008-docs-update.md

## Goal

Document implemented event-to-apply target-proxy lifecycle timing.

## Files Allowed To Modify

- `Docs/contracts/target-proxy.md`
- `Docs/flows/target-proxy-lifecycle.md`
- `Docs/flows/runtime-frame.md`
- `Docs/contracts/spawn-events-and-commands.md` only for a concise sibling-pipeline cross-reference.

## Facts To Document

- Registration queues create; handle resolves on a later simulation tick.
- Position, shape, and resource updates queue then apply before spatial hashing.
- Deletes queue and apply in Presentation after `CombatApplyBridge`.
- Keep existing LateUpdate-order TODOs unchanged.

## Validation

- Search docs for obsolete synchronous proxy lifecycle wording and preserve TODOs.
