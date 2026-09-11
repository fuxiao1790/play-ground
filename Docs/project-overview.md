# Project Overview

## Agent instructions

Rules apply to all agents working in this project:

- Use caveman speech. Follow [.agent/caveman.md](../.agent/caveman.md).
- Always defer test execution to the user. Agents must not run tests or invoke
  the Unity test runner.
- Name the tests to run, not a command. Give the platform (EditMode or
  PlayMode), the test class, and the specific test methods when only part of a
  class applies.
- Require test results exported as an XML result file under `Logs/`, named per
  [testing.md](./testing.md) §*Result Files*.
- Review the user-provided XML before reporting test results or claiming tests
  passed. Do not substitute logs, console output, or screenshots.

This page is a simple index to the documentation set. Use it to jump to the
section that matches the topic you need.

## Working guidance

- Use [folder-structure.md](./folder-structure.md) to quickly find files.
- Review [architecture/index.md](./architecture/index.md) and [coding-standards.md](./coding-standards.md) before making code changes.
- Review [reference/simulation/ecs-notes.md](./reference/simulation/ecs-notes.md) before suggesting or making code changes.

## Critical info

- This project is a Unity 6000.4 2D URP top-down action game.
- The core design target is extreme attack scaling with many projectiles, AOEs,
  and chained effects.
- Performance is a primary constraint, so simulation and spawn design should be
  checked against the ECS and profiling docs.
- The project uses Unity-native gameplay code for authored content and ECS/DOTS
  for high-count combat simulation.

## Start here

- [UI architecture](./ui.md)
- [Architecture index](./architecture/index.md)
- [Layer rules](./architecture/layer-rules.md)
- [Data flow overview](./architecture/data-flow-overview.md)
- [Numeric modifiers](./reference/architecture/numeric-modifiers.md) - the
  general base/added/increased/multiplier fold; read before adding any new
  numeric stat modifier anywhere in the codebase, not just skills.
- [Folder structure](./folder-structure.md)

## Design and reference

- [Gameplay design](./reference/design/gameplay.md)
- [Visual style reference](./visual/style-reference.md)
- [Skill system](./reference/game-logic/skill-system.md)
- [Skill stat modifiers](./reference/game-logic/skill-modifiers.md)
- [Spawn system](./reference/game-logic/spawn-system.md)
- [Simulation index](./reference/simulation/index.md)

## Contracts and flows

- [Spawn events and commands](./contracts/spawn-events-and-commands.md)
- [Target proxy](./contracts/target-proxy.md)
- [Combat hit and tick results](./contracts/combat-hit-and-tick-results.md)
- [Sound events](./contracts/sound-events.md)
- [Collision to combat result](./flows/collision-to-combat-result.md)

## Process and rules

- [Coding standards](./coding-standards.md)
- [Testing](./testing.md)
- [Performance](./performance.md)
- [Profiling](./profiling.md)
- [Release](./release.md)
- [Todo](./todo.md)
- [Memory index](./memory/index.md)
