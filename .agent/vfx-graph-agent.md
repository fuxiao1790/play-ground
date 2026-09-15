---
name: vfx-graph-agent
description: Inspect Unity VFX Graph assets through AgentVFX CLI output instead of screenshots or serialized .vfx text. Use when understanding an existing VFX Graph; not for graph authoring or mutation.
---

# VFX Graph Inspection

Use AgentVFX for every VFX Graph analysis. Do not open a `.vfx` file as text
and do not infer graph structure from screenshots.

First verify Unity connection:

```powershell
unity pipeline list
unity command vfx_ping
```

Inspect authored graphs only under `Assets/Vfx/`:

```powershell
unity command vfx_graph_read --assetPath "Assets/Vfx/LineSeg/MagicBoltTrail.vfx" --json
```

`vfx_graph_read` returns nodes, node settings, root and nested slots, data
connections, context flow connections, and exposed blackboard properties.
`valueJson` represents primitives directly; curves, gradients, and asset
references return structured JSON.

Use `vfx_slot_read <slotId>` only for a slot id returned by a fresh graph read.
Use `vfx_types_list` and `vfx_type_describe <typeId>` to understand current
Unity VFX Graph types.

This bridge is read-only. It has no create, save, node-edit, slot-write,
connection-edit, blackboard-edit, compile, or preview commands. If required
information is not present in command output, report the limitation; do not
fall back to screenshot or `.vfx` serialization inspection.
