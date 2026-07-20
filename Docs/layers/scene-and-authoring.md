# Scene And Authoring

## Purpose

Own Unity-authored objects, low-count runtime actors, prefab composition,
ScriptableObject configuration, Physics2D movement/collision, camera, play area,
and authored presentation setup.

## Owns

- `GameRoot`, `PlayerRoot`, `MobRoot`, `GameplayCamera`, `PlayAreaRoot`,
  `DebugOverlay`, and authored scene roots.
- Player and mob Transforms, Rigidbody2D, Colliders, Animator, SpriteRenderer,
  GameObject lifetime, and low-count body/environment collision.
- ScriptableObjects and prefabs used as authoring templates.
- Scene-level references and setup validation.
- `UIDocument`, UI Toolkit presentation roots, and UI input routing.

UI implementation lives in `Packages/com.playground.skill-ui/` and may depend
on Game Logic. Game Logic and ECS Simulation must not depend on that package.

## Does Not Own

- High-count projectile, AOE, status, hit, or VFX simulation.
- Projectile/AOE entity allocation, reuse, movement, collision, or lifetime.
- ECS-owned target health/status once represented by target proxy combat state.
- Spawn expansion math or ECS command application.

## Inputs

- Player input.
- Authored prefabs, ScriptableObjects, scenes, layers, and serialized fields.
- Presentation results from the combat bridge/presentation layer.

## Outputs

- Managed spawn requests through the combat bridge.
- Target registration and target proxy push/delete calls.
- Actor presentation changes such as animation, hurt feedback, and death state.

## Allowed Dependencies

- May call [Combat Root API](../contracts/combat-root-api.md).
- May produce [Spawn Requests](../contracts/spawn-requests.md).
- May participate in [Target Proxy](../contracts/target-proxy.md) lifecycle.
- May depend on game logic services and authored configuration.

## Forbidden Dependencies

- Must not create projectile or AOE ECS entities directly.
- Must not bypass event -> command -> apply.
- Must not make high-count projectile/AOE collision authoritative through live
  trigger callbacks.
- Must not make simulation jobs read live Unity objects.

## Main Systems / Modules

- `Assets/Scripts/Game/GameRoot.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Mob/MobRoot.cs`
- `Assets/Scripts/Camera/GameplayCamera.cs`
- `Assets/Scripts/Level/PlayAreaRoot.cs`

## Related Contracts

- [Combat Root API](../contracts/combat-root-api.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Target Proxy](../contracts/target-proxy.md)
- [Skill Loadout Editing](../contracts/skill-loadout-editing.md)

## Related Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)
- [Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md)
- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Skill Loadout Edit](../flows/skill-loadout-edit.md)

## Notes / TODOs

- Gameplay direction reference:
  [../reference/design/gameplay.md](../reference/design/gameplay.md).
