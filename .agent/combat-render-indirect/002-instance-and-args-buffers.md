# 002 — Instance And Indirect-Args Buffers

## Change

Introduce the `CombatInstanceData` struct and the two `GraphicsBuffer`s that back the single
indirect draw. These live on `CombatBatchedRenderSystem` (owned/disposed there); this task
defines their shape, lifecycle, and the indirect-args population. Task 003 wires them into
`OnUpdate`.

## `CombatInstanceData`

Per the Data Contract (index.md). Place it next to the render component structs in
`CombatRenderComponents.cs`, or as a nested type in the render system — either is fine as long
as the C# layout byte-matches the HLSL struct.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CombatInstanceData
{
    public Matrix4x4 objectToWorld; // 64B, Unity column-major
    public Vector4   uvRect;        // 16B
}                                   // stride = 80
```

Add a compile-time-ish guard (comment or an `Assert` in `OnCreate`) that
`UnsafeUtility.SizeOf<CombatInstanceData>() == 80` so a future field addition can't silently
desync the stride from the shader.

## Buffers (fields on the render system)

```csharp
private GraphicsBuffer _instanceBuffer;   // Target.Structured, stride 80
private GraphicsBuffer _argsBuffer;        // Target.IndirectArguments, one command
private NativeList<CombatInstanceData> _instances; // Allocator.Persistent, filled each frame
private int _instanceCapacity;
```

### Lifecycle

- `OnCreate`: create `_instances` (`Allocator.Persistent`), create `_argsBuffer`
  (`GraphicsBuffer.Target.IndirectArguments`, count 1,
  stride `GraphicsBuffer.IndirectDrawIndexedArgs.size` = 20). Do **not** create the instance
  buffer yet (size unknown); create it lazily on first non-empty frame or at a small initial
  capacity (e.g. 1024).
- `OnDestroy`: dispose `_instances`, `_instanceBuffer`, `_argsBuffer` (null-guard each).

### Growth (`EnsureInstanceCapacity(int count)`)

```csharp
if (count <= _instanceCapacity && _instanceBuffer != null) return;
int newCap = math.max(count, _instanceCapacity == 0 ? 1024 : _instanceCapacity * 2);
while (newCap < count) newCap *= 2;
_instanceBuffer?.Dispose();
_instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCap, 80);
_instanceCapacity = newCap;
// buffer identity changed → material/matProps must rebind it (task 003/004)
_rebindBuffer = true;
```

Grow-by-doubling keeps reallocation rare (log growth) and never shrinks — matches the pooled,
non-shrinking nature of the entity set. Recreating the buffer invalidates any prior
`SetBuffer` binding, so flag a rebind.

## Indirect args population

For an **indexed** mesh (the shared unit quad has an index buffer), use
`IndirectDrawIndexedArgs`:

```csharp
var args = new GraphicsBuffer.IndirectDrawIndexedArgs
{
    indexCountPerInstance = SharedMesh.GetIndexCount(0), // 6 for a quad
    instanceCount         = (uint)activeCount,
    startIndex            = SharedMesh.GetIndexStart(0),
    baseVertexIndex       = SharedMesh.GetBaseVertex(0),
    startInstance         = 0
};
_argsBuffer.SetData(new[] { args });
```

`instanceCount` is the only field that changes per frame; the mesh-derived fields are constant
after the mesh exists (could be cached, but a 20-byte `SetData` per frame is negligible).

## Acceptance Criteria

- `sizeof(CombatInstanceData) == 80` and field order matches the HLSL struct.
- `_instanceBuffer` grows monotonically to fit the peak active count, never shrinks, and is
  always ≥ the current active count before it is bound/drawn.
- `_argsBuffer` holds exactly one `IndirectDrawIndexedArgs` with `instanceCount == activeCount`
  and `indexCountPerInstance == SharedMesh.GetIndexCount(0)`.
- All three GPU/native resources are disposed in `OnDestroy` with no leak on domain reload.

## Dependencies

Data Contract (index.md). Consumed by task 003.

## Scope

Small.
