---
name: 006-cli-commands
description: "[CliCommand] surface over AgentVfxApi — UNVERIFIED against real Unity.Pipeline source"
---

# 006 — CLI Commands

## Goal
Register the guide's target command surface
(`vfx_ping`, `vfx_graph_read/create/save`, `vfx_types_list`, `vfx_type_describe`,
`vfx_node_*`, `vfx_slot_*`, `vfx_blackboard_*`, `vfx_compile`, `vfx_errors`) as
`[CliCommand]`s.

## File
- `Assets/AgentVFX/Editor/AgentVfxCommands.cs`

## Status: unverified
`com.unity.pipeline` is not installed in this project and `unity` is not on PATH
(confirmed this session). This file is written to the guide's example signature
(`[CliCommand("name", "description", MainThreadRequired = true)]` from
`Unity.Pipeline.Commands`) only. **It will not compile until the package is
installed**, and the attribute's real namespace/parameters have not been checked
against source. Do not treat this task as done until the user has installed the
CLI/Pipeline and it compiles.

## Acceptance Criteria
- After the user installs `com.unity.pipeline` and Unity recompiles, `unity
  command` lists every command above and `unity command vfx_ping` returns
  `typeof(VFXGraph).FullName`.

## Dependencies
005. Blocked externally on user's CLI/Pipeline install.
