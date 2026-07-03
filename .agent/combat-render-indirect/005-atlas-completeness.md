# 005 — Atlas Completeness (single page, every sprite)

## Change

The end goal requires the single-page atlas to contain **every** sprite any ECS combat entity
may spawn — because `Register(...)` throws if a sprite isn't a packable of the configured atlas,
and one indirect draw samples exactly one atlas texture. This task is the content + enforcement
side; it is **not** code-only and needs the Unity Editor.

## Why it matters for this rework specifically

With `RenderMeshIndirect` there is one shared material → one `_MainTex` → one atlas page for the
entire draw. If any combat sprite lives outside that page, it either throws at registration
(fail-loud, current behavior) or — worse if the guard were relaxed — would sample the wrong
texture. So "one draw call for everything" structurally *requires* "one atlas holds everything."
The single-page guard in `Register(...)` (throws if a later sprite resolves a different packed
texture) already enforces the single-page half; this task ensures completeness so the guard
never fires in a real scene.

## Work

1. **Enumerate every combat sprite** the ECS can spawn — walk each projectile/AOE template/config
   registered through `PlayerSkillDriver.RegisterProjectileTypes`/`RegisterAoeTypes` →
   `CombatRoot.Register*` and collect the `Sprite` each passes to `Register(...)`.
2. **Add all of them as packables** of the single combat `SpriteAtlas` asset assigned to
   `CombatRoot.combatSpriteAtlas` (the project already has `Assets/Atlas/Skills.spriteatlasv2`;
   confirm it is the one assigned in `BenchmarkLarge.unity` — it currently is — and that it holds
   every sprite, not just some).
3. **Force single page:** set the atlas's Max Texture Size large enough that all sprites pack
   into one page; verify via Pack Preview that page count == 1. If content can't fit one page,
   that is a hard design conflict with the single-draw goal — surface it, don't silently accept
   multi-page.
4. **Read/Write not required** for the indirect path (no `GetPixels` — UV comes from
   `sprite.rect`), so the atlas-plan's `isReadable` edits are unrelated here; leave them.
5. **Runtime assert (optional, cheap):** in `Register(...)` the existing "different packed
   texture ⇒ throw" already asserts single-page; no new code needed, but note it as the
   guarantee this task upholds.

## Acceptance Criteria

- Every sprite reachable through skill registration is a packable of the one assigned atlas, and
  `Register(...)` throws for none of them at runtime.
- Pack Preview shows a single page (one texture) for that atlas.
- `BenchmarkLarge.unity`'s `CombatRoot.combatSpriteAtlas` references that complete atlas.

## Dependencies

Independent of 001–004 (content, not compilation). Gates real gameplay, not the build.

## Scope

Content/editor task — cannot be completed by editing files blindly; needs the Editor's Sprite
Atlas packer and Pack Preview.
