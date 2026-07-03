# Atlas Configuration

*(Was "Atlas Packer" in the original plan; repurposed 2026-07-03 per explicit user direction:
single-page, manually-assembled atlas, no runtime packing. `CombatSpriteAtlasPacker` — the file
this task originally produced — is deleted, not modified.)*

*(Revised again 2026-07-03, same day: the field was originally a plain `Texture2D` with sprites
manually sliced from one big texture via Sprite Editor "Multiple" mode. User feedback: "this field
does not accept sprite atlas type" — the field must be a real `UnityEngine.U2D.SpriteAtlas` asset
(the same asset type as the project's existing `Assets/Atlas/Skills.spriteatlasv2`), not a raw
texture. This is a materially different Unity mechanism, described below.)*

*(Revised a third time 2026-07-03, same day: the first `SpriteAtlas` pass assumed a
`SpriteAtlas.GetTexture()` method to resolve the packed texture. That method does not exist —
caught by a real compiler error (`CS1061`) from the user. `SpriteAtlas`'s actual documented
runtime lookup API is `GetSprite(string name)`, which returns null both when the atlas isn't
packed yet and when the sprite isn't a packable. The design below reflects the corrected
`GetSprite`-based approach.)*

## Change

The combat sprite atlas is **one manually-assembled `SpriteAtlas` asset** (`Window > 2D > Sprite
Atlas` in the Editor), not something built at runtime. Individual combat sprites — each its own
project asset — are added to the atlas's packables list and packed (Editor "Pack Preview", or
automatically at build time for `.spriteatlasv2` / Sprite Atlas V2 assets, which pack eagerly
without needing a manual step). Once packed, Unity's Sprite Packing system transparently
redirects every packed `Sprite`'s `.texture` (and `.rect`, relative to that texture) to the
atlas's packed page — existing code that references those sprite assets (e.g. a skill prefab's
`SpriteRenderer.sprite`) needs no changes to pick this up.

This task adds the plumbing to configure and validate against that atlas — no packing algorithm.

### `CombatRoot` — serialized field + test hook

```csharp
[Header("Render Atlas")]
[SerializeField] private SpriteAtlas combatSpriteAtlas;

public void ConfigureAtlas(SpriteAtlas atlas) => combatSpriteAtlas = atlas;
```

In `Awake()`, after resolving `_renderRegistry` and before `BuildProjectileRenderResources()`:

```csharp
_renderRegistry?.ConfigureAtlas(combatSpriteAtlas);
```

(Mirrors the existing `Configure(Sprite sprite)` pre-`Awake` test-configuration pattern.)

### `CombatRenderResourceRegistry` — configuration + validation

```csharp
public SpriteAtlas Atlas { get; private set; }
public Texture2D AtlasTexture { get; private set; }

public void ConfigureAtlas(SpriteAtlas atlas)
{
    EnsureSharedResources();
    Atlas = atlas;
    AtlasTexture = null; // resolved lazily in Register(...) via Atlas.GetSprite(...).texture
}
```

`Register(...)` (see task 003 for the full method) must, before computing anything:

1. Throw `InvalidOperationException` if `Atlas == null` (atlas not configured yet).
2. Look up `Sprite packedSprite = Atlas.GetSprite(sprite.name);` and throw
   `InvalidOperationException` if it's null — this is the *only* membership check, and it
   deliberately does not try to distinguish "atlas not packed yet" from "sprite not a packable
   of this atlas": `GetSprite` returns null in both cases and there is no separate public API to
   tell them apart, so a single honest error message covers both. (There is no
   `SpriteAtlas.GetTexture()` method — an earlier draft of this task assumed one; it does not
   exist and does not compile.)
3. Resolve `Texture2D atlasTexture = packedSprite.texture;` and, if a previous `Register(...)`
   call already resolved a different `AtlasTexture`, throw (defends the single-page invariant —
   see index.md's "Constraints And Invariants").

Then compute the UV rect directly from the packed sprite/texture — no packing, no padding:

```csharp
Vector4 uvRect = new(
    packedSprite.rect.x / atlasTexture.width,
    packedSprite.rect.y / atlasTexture.height,
    packedSprite.rect.width / atlasTexture.width,
    packedSprite.rect.height / atlasTexture.height);
```

Note this uses `packedSprite` (the atlas's own copy, returned by `GetSprite`), not the original
`sprite` parameter — `packedSprite.rect`/`.pixelsPerUnit` are guaranteed accurate post-pack.

## Acceptance Criteria

- `CombatRoot` exposes `Combat Sprite Atlas` in the Inspector (a `SpriteAtlas` field, so a real
  `.spriteatlasv2` asset can be dragged in — a plain `Texture2D` field cannot accept one) and a
  matching `ConfigureAtlas(SpriteAtlas)` public method for pre-`Awake` test/programmatic setup.
- `CombatRenderResourceRegistry.Register(...)` throws a clear, actionable error message for each
  distinct failure it can actually detect: atlas not configured; sprite not returned by
  `Atlas.GetSprite(name)` (covers both "not packed yet" and "not a packable"); or a resolved
  texture that contradicts a previously registered kind's texture (multi-page atlas guard).
- `Register(...)` never silently falls back to a different texture, never packs the sprite in,
  never proceeds with a wrong/degenerate UV rect.
- Computing the UV rect from `packedSprite.rect`/`atlasTexture.width`/`height` requires no
  CPU-side pixel access (`GetPixels`/`SetPixels`) — unlike the deleted runtime packer, this task
  does not need source textures to have "Read/Write Enabled." (Note: this makes the earlier
  `isReadable` fix applied to 7 texture assets during the packer-based implementation unnecessary
  going forward — harmless to leave as-is, but no longer load-bearing for this plan.)

## Test Fixtures — Real Asset Requirement

`SpriteAtlas.GetSprite(name)`/the `sprite.texture` redirection only resolve to something non-null
after Unity's actual asset-pipeline packing step, which requires real, on-disk texture + atlas
assets. An in-memory `Sprite.Create(...)` sprite (what every PlayMode test fixture previously
used) can never satisfy `Register(...)`'s validation, because it was never packed into anything.
There is no supported pure-runtime way to fabricate a working packed `SpriteAtlas` from C# alone.

Resolution (user-directed): a real test atlas asset lives at
`Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2`, backed by a real source texture at
`Assets/Tests/TestAssets/CombatAtlasTestSource.png`. `Assets/Tests/PlayMode/CombatAtlasTestFixture.cs`
(new, `#if UNITY_EDITOR`-guarded) loads both via `AssetDatabase` and exposes them as
`CombatAtlasTestFixture.Atlas`/`CombatAtlasTestFixture.Sprite`; every PlayMode fixture that used to
call `Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f)` now
references `CombatAtlasTestFixture.Sprite` instead, and every `ConfigureAtlas(...)` call passes
`CombatAtlasTestFixture.Atlas`. See task 006 for the exact asset specification the real texture
must match (1×1 px sprite region, pivot (0.5, 0.5), pixels-per-unit 100) so every existing
hand-derived assertion (e.g. the `(0.06, 0.08)` AOE matrix-scale values from task 004's fix) stays
correct without re-deriving.

## Dependencies

None.

## Scope

Small — this replaced a Medium-scope packing task with a small configuration/validation one.
## Post-Editor Continuation

`Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2` now exists. The PlayMode fixture no
longer requires a hardcoded `CombatAtlasTestSource.png` path; it loads the first imported `Sprite`
dependency of that atlas and derives expected native size from that sprite.
