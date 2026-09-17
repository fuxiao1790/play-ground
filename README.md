# Play Ground

An experimental top-down action game built in Unity. Its combat is designed to
scale from individual attacks to dense screens of projectiles, areas of effect,
and chained reactions while keeping simulation performance in focus.

## At a glance

- **Engine:** Unity 6000.4
- **Rendering:** 2D Universal Render Pipeline (URP)
- **Gameplay approach:** Unity-authored content paired with ECS/DOTS combat
  simulation for high entity counts

## Getting started

Open this folder in Unity Hub with Unity `6000.4.8f1`. Unity will restore the
packages listed in `Packages/manifest.json` when the project opens.

## Explore the project

Start with the [project documentation](Docs/project-overview.md) for a map of
the codebase and its design references. These are especially useful entry
points:

- [Gameplay design](Docs/reference/design/gameplay.md)
- [Architecture](Docs/architecture/index.md)
- [UI architecture](Docs/ui.md)
- [Folder structure](Docs/folder-structure.md)

## Contributing

Before changing gameplay or simulation code, review the project's
[coding standards](Docs/coding-standards.md), [architecture guidance](Docs/architecture/index.md),
and [ECS notes](Docs/reference/simulation/ecs-notes.md). Performance is a core
constraint: consult [performance](Docs/performance.md) and
[profiling](Docs/profiling.md) guidance when working on combat or spawning.

## License

See [LICENSE](LICENSE).
