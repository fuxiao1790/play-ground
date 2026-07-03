# Atlas Configuration

*(Was "Atlas Packer" in the original plan; repurposed 2026-07-03 per explicit user direction:
single-page, manually-assembled atlas, no runtime packing. `CombatSpriteAtlasPacker` — the file
this task originally produced — is deleted, not modified.)*

## Change

The combat sprite atlas is **one manually-assembled texture asset**, not something built at
runtime. An artist/developer slices every combat sprite kind as a separate `Sprite` out of one
shared source texture in the Unity Editor (standard multi-sprite texture import: Sprite Mode
"Multiple", sliced via the Sprite Editor). Each skill prefab's `SpriteRenderer.sprite` then
points at one of those slices, so `sprite.texture` is the *same texture object* for every combat
sprite.

This task adds the plumbing to configure and validate against that texture — no packing
algorithm.

### `CombatRoot` — serialized field + test hook

```csharp
[Header("Render Atlas")]
[SerializeField] private Texture2D combatAtlasTexture;

public void ConfigureAtlas(Texture2D atlasTexture) => combatAtlasTexture = atlasTexture;
```

In `Awake()`, after resolving `_renderRegistry` and before `BuildProjectileRenderResources()`:

```csharp
_renderRegistry?.ConfigureAtlas(combatAtlasTexture);
```

(Mirrors the existing `Configure(Sprite sprite)` pre-`Awake` test-configuration pattern.)

### `CombatRenderResourceRegistry` — configuration + validation

```csharp
public Texture2D AtlasTexture { get; private set; }

public void ConfigureAtlas(Texture2D atlasTexture)
{
    EnsureSharedResources();
    AtlasTexture = atlasTexture;
    SharedMaterial.mainTexture = atlasTexture;
}
```

`Register(...)` (see task 003 for the full method) must, before computing anything:

1. Throw `InvalidOperationException` if `AtlasTexture == null` (atlas not configured yet).
2. Throw `InvalidOperationException` if `sprite.texture != AtlasTexture` (this specific sprite
   was not sliced from the configured atlas — the "skills register themselves, if atlas doesn't
   have the sprite, throw error" requirement). This is a reference-equality check: it only
   correctly detects "sprite belongs to the atlas" if the sprite asset really is a slice of the
   *same* `Texture2D` object assigned as `combatAtlasTexture`, not merely a visually similar
   texture.

Then compute the UV rect directly (no packing, no padding):

```csharp
Vector4 uvRect = new(
    sprite.rect.x / AtlasTexture.width,
    sprite.rect.y / AtlasTexture.height,
    sprite.rect.width / AtlasTexture.width,
    sprite.rect.height / AtlasTexture.height);
```

## Acceptance Criteria

- `CombatRoot` exposes `Combat Atlas Texture` in the Inspector (single `Texture2D` field) and a
  matching `ConfigureAtlas(Texture2D)` public method for pre-`Awake` test/programmatic setup.
- `CombatRenderResourceRegistry.Register(...)` throws a clear, actionable error message if called
  before `ConfigureAtlas(...)`, or with a sprite whose `.texture` isn't the configured atlas.
- `Register(...)` never silently falls back to a different texture, never packs the sprite in,
  never proceeds with a wrong/degenerate UV rect.
- Computing the UV rect from `sprite.rect`/`AtlasTexture.width`/`height` requires no CPU-side
  pixel access (`GetPixels`/`SetPixels`) — unlike the deleted runtime packer, this task does not
  need source textures to have "Read/Write Enabled." (Note: this makes the earlier `isReadable`
  fix applied to 7 texture assets during the packer-based implementation unnecessary going
  forward — harmless to leave as-is, but no longer load-bearing for this plan.)

## Dependencies

None.

## Scope

Small — this replaced a Medium-scope packing task with a small configuration/validation one.
