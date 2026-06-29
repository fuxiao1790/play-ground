# 004 — Stats display MonoBehaviour

## Goal
`CombatStatsDisplay : MonoBehaviour` mounted on a scene GameObject. It binds to
the singleton entity through `CombatStatsGatherSystem` in `OnEnable`, receives the
pushed snapshot via `Apply`, and renders it. Unbinds in `OnDisable`.

## File
- New `Assets/Scripts/System/Stats/CombatStatsDisplay.cs`.

## Shape

```csharp
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Stats
{
    // Scene-side debug widget. Bound to the CombatStatsSingleton entity through
    // CombatStatsBinding; CombatStatsGatherSystem pushes the snapshot via Apply.
    public sealed class CombatStatsDisplay : MonoBehaviour
    {
        [SerializeField] private Vector2 screenOffset = new Vector2(10f, 10f);

        private CombatStatsGatherSystem _system;
        private CombatStatsSingleton _stats;
        private bool _bound;

        // OnEnable: cross-system registration belongs here (coding-standards
        // "Awake vs OnEnable Boundary").
        private void OnEnable()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            _system = world?.GetExistingSystemManaged<CombatStatsGatherSystem>();
            if (_system == null)
            {
                Debug.LogWarning("[CombatStatsDisplay] CombatStatsGatherSystem not found; stats will not display.");
                return;
            }

            _system.Bind(this);
            _bound = true;
        }

        private void OnDisable()
        {
            if (_bound) _system?.Unbind(this);
            _bound = false;
            _system = null;
        }

        internal void Apply(in CombatStatsSingleton stats) => _stats = stats;

        private void OnGUI()
        {
            var rect = new Rect(screenOffset.x, screenOffset.y, 360f, 90f);
            GUI.Label(rect,
                $"Spawn ECB:   {_stats.EntitiesSpawnedViaEcb}\n" +
                $"Spawn reuse: {_stats.EntitiesSpawnedViaReuse}\n" +
                $"Hit events:  {_stats.HitEventsCreated}\n" +
                $"VFX events:  {_stats.VfxEventsCreated}");
        }
    }
}
```

## Scene wiring (manual step)
1. Create an empty GameObject in the combat scene, e.g. `CombatStatsDisplay`.
2. Add the `CombatStatsDisplay` component.
3. Press play; the label shows live counts (zeros until combat runs).

## Constraints / rationale
- Binds via explicit `Bind`/`Unbind`, not scene scans (coding-standards "Unity
  Object Access").
- `OnEnable`/`OnDisable` symmetry keeps the managed binding clean when the object
  is toggled or the scene reloads.
- `OnGUI` is chosen for zero scene wiring; can be swapped for a serialized
  `TMP_Text` driven from `Apply` without touching the system or components.
- Per-object debug widget on its own GameObject — does not write to the shared
  `DebugOverlay` label (coding-standards "Debug UI Ownership").

## Edge cases
- If the default world or system is not ready at `OnEnable` (unusual for
  scene-loaded objects, since the default world is created during initialization),
  it logs once and renders zeros. If this proves flaky in practice, retry the bind
  in `Start`.

## Acceptance criteria
- Component mounts and renders without errors.
- Counts update live during combat.
- Disabling the GameObject clears `CombatStatsBinding.Display` (no dangling
  reference); re-enabling re-binds.

## Dependencies
001 (components), 003 (`Bind`/`Unbind`, push).

## Scope
Small.
