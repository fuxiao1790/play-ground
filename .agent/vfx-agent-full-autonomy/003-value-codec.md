# 003 - Unified AgentVFX Value Codec

## Goal

Replace separate convenience/executor conversion paths with one JSON-to-CLR
contract. Preserve existing bare JSON while adding curve and engine-object
support.

## Dependencies

001-002.

## Files

- Create `Assets/AgentVFX/InternalAccess/AgentVfxValueCodec.cs`.
- Refactor `AgentVfxJson.cs` into a thin compatibility wrapper over the new
  codec, or remove it and update all callers in the same change.
- Update `AgentVfxSlotOps.cs` and setting snapshot/configuration callers to use
  the shared codec.

## Supported Shapes

- JSON scalars: null, bool, all signed/unsigned integral types, float, double,
  decimal, char, and string; honor nullable expected types.
- Existing untagged Unity structs: Vector2/3/4, Color, Rect. Add Quaternion,
  Bounds, Matrix4x4, and Keyframe with explicit field validation.
- `{"$enum":{"type":"...","value":"..."}}` and `{"$type":"..."}`.
- `{"$local":"name"}` for request-local aliases and
  `{"$ref":"node:1"}`/`slot:`/`data:`/`obj:` for durable handles.
- `{"$asset":{"path":"Assets/...","type":"UnityEngine.Texture2D"}}`.
  Decode verifies project-relative path, existence, expected-type assignment,
  and never writes the referenced asset.
- `{"$curve":{"preWrapMode":"...","postWrapMode":"...","keys":[...]}}`
  containing complete Keyframe data: time, value, tangents, weights, and
  `WeightedMode`.
- Tagged Gradient containing mode, color keys, and alpha keys.
- Arrays and `List<T>` decoded from JSON arrays using real expected element
  type; local refs are valid elements.

## Encoding Rules

- Check `VFXModel` before `UnityEngine.Object` because graph models are also
  Unity objects.
- Encode persistent Unity assets as `$asset`, curves/gradients structurally,
  supported structs structurally, collections element-by-element, and approved
  non-model references as scoped `$ref` handles.
- Unsupported value types throw with exact CLR type. Never emit `{}` or silently
  discard fields.
- `Decode(Encode(value))` must preserve every supported value semantically.

## Acceptance Criteria

- Existing primitive/vector slot-set behavior remains wire-compatible.
- `AnimationCurve` round-trip preserves wrap modes and every Keyframe field.
- `Texture2D` read returns `$asset`; setting that value back resolves same asset.
- VFX model encoding uses same id as `vfx_graph_read`/convenience commands.
- Invalid asset type/path and unsupported struct errors are explicit.

## Scope

Large. Central compatibility-sensitive refactor.
