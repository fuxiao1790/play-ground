# Single Thread Expansion

## Change

Replace projectile and AOE expansion `NativeStream` output with owned
`NativeList<TCommand>` containers filled by one Burst `IJob`.

## Acceptance Criteria

- Projectile expansion writes ordered `ProjectileSpawnCommand` values into one
  list.
- AOE expansion writes ordered impact and lingering `AoeSpawnCommand` values
  into separate lists.
- Expansion still drains producer queues and scope buffers after producers
  complete.
- Spawn math remains in expansion systems.

## Dependencies

None.

## Scope

Medium.
