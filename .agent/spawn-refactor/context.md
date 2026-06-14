this is what i was envisioning.
on spawn, internal or external, new entities are enqueued to something like a map of archetype to spawn config queue.
the spawn system simply loops through the map key set to create entity queries.
schedule workers on all results and each worker loops through their chunk to dequeue and reuse dead slots.
remaining items in queues in the map are handled by ecb playback


Got it. That's more parallel than what I built — the slot-claiming and resetting happen inside the worker instead of on the main thread. Key difference:

Current: main thread claims slots (TryTakeDeadSlot per request, sequential), then schedules parallel reset.

Your design: workers claim AND reset inside the job, per-chunk.

The tricky piece: "each worker dequeues" from a shared config queue across multiple chunk workers requires a concurrent consumer. NativeQueue has no parallel reader. Two solutions:

Atomic index into NativeArray<SpawnConfig> — NativeReference<int> counter per archetype, workers do Interlocked.Add(ref counter, 1) to atomically claim an index. No two workers get the same slot. After jobs: counter → configs.Length are ECB creates.

Pre-partition — main thread counts dead slots per chunk (one pass), assigns config slices to each chunk, then jobs run independently with no shared state.

Option 1 is simpler to set up. Option 2 has no atomics but requires a pre-pass.

The archetype map key also needs to include (scope, typeId) since CombatRenderScope/CombatRenderTypeId are shared components that split chunks — a chunk's worker can only claim configs that match its chunk's scope and typeId. You can read those from the chunk via chunk.GetSharedComponent<T>().

Want me to implement this? It's a meaningful rewrite of both spawn systems — the SpawnRequestElement buffers likely get replaced by the archetype-keyed config map fed from ProjectileRoot.Spawn() / AoeRoot.Spawn() directly.

Option 1 — Atomic cursor (shared across workers):


// each worker, per disabled entity:
int claimed = Interlocked.Add(ref Cursor.Value, 1) - 1;
if (claimed >= Configs.Length) return;
ResetEntity(entities[i], Configs[claimed]);
Workers race to grab the next config. No pre-pass needed. Downside: memory fence per claim; also needs a cursor per (scope, typeId) key since chunks are already split by shared components and you can't have chunk-A claim a config meant for chunk-B's scope.

Option 2 — Pre-partitioned slices (no shared state):


// main thread before scheduling: count dead slots per chunk, assign slice
slices[chunkIdx] = new int2(startInConfigs, countToConsume);

// worker gets its own slice, no contention:
int idx = slice.x;
for each disabled entity:
    ResetEntity(entities[i], Configs[idx++]);
Main thread does one pre-pass to count dead slots per chunk, then cuts the config array into per-chunk slices. Workers run with zero shared state, no atomics.

The key insight for both: chunks are already split by (CombatRenderScope, CombatRenderTypeId) since those are shared components. So grouping the config map by (scope, typeId, hasChildSpawner) means each chunk's worker only ever sees configs intended for it — no cross-scope contamination.

## Option 2A vs 2B detail

### Option 2A — main-thread pre-count

```
frame work:
  1. [main] collect spawn requests into NativeArray<SpawnConfig> per archetype key
  2. [main] get chunk array from dead-slot query (cheap — already cached)
  3. [main] iterate chunks, count disabled per chunk → assign slice per chunk
  4. [job]  IJobChunk: each chunk resets its assigned slice of entities
  5. [main] ECB create for unmatched tail of configs
```

The count step (3) is a simple bit-popcount over the chunk's enabled mask —
roughly `chunk.Count` iterations, very fast. For typical frame counts (0–20
spawns, 2–4 relevant chunks) this is negligible on the main thread.

### Option 2B — count in a parallel job

```
frame work:
  1. [main] collect spawn requests into NativeArray<SpawnConfig> per archetype key
  2. [main] get chunk array from dead-slot query
  3. [job1] IJobChunk: count disabled per chunk → NativeArray<int> deadCounts
  4. [main] prefix-sum deadCounts → NativeArray<int2> slices (start, count)
            ECB create for unmatched tail
  5. [job2] IJobChunk: each chunk resets its assigned slice
```

Two scheduled jobs (+ prefix sum) instead of one. The prefix sum must run
after job1 but before job2, so it blocks on the main thread between the two.

### When 2B wins over 2A

2B only pays off if the count step dominates. In practice:
- Each chunk holds ~128 entities max (Unity default)
- Counting disabled bits = ~2 SIMD ops per chunk on modern CPUs
- Scheduling overhead for an extra `IJobChunk` >> counting cost for <10 chunks

**Conclusion: 2A is almost always faster here.** 2B only makes sense if there
are hundreds of relevant chunks, which would mean thousands of dead projectiles
pooled across many (scope, typeId) combos — an unlikely hot path.

---

Both files written. Summary of what changed:

Both systems — replaced sequential main-thread slot claiming with grouped IJobChunk:

Added ProjectileSpawnKey / AoeSpawnKey structs to group requests by (scopeIndex, typeId[, hasChildSpawner])
OnUpdate now groups all requests into Dictionary<Key, List<Request>> (lists pooled to avoid GC), then per key dispatches one filtered IJobChunk
2A (active): AssignSlices2A iterates filtered chunks on the main thread via ToArchetypeChunkArray + GetEnabledMask popcount, builds NativeList<int2> slices with (startIdx, count) per chunk, schedules ProjectileSpawnJob/AoeSpawnJob
2B (commented out, compiles): ScheduleCountJob2B + countHandle.Complete() + PrefixSumSlices2B — same IJobChunk after prefix sum
Job uses [NativeDisableContainerSafetyRestriction] on all writable ComponentTypeHandle fields — safe because shared component filter guarantees disjoint chunks across keys
NativeList<T>.Dispose(spawnHandle) chained for both configs and slices; synchronous dispose used when claimed == 0
Cold creates via ECB unchanged; VFX buffer fill in AOE done during grouping pass (same timing as before)
Profiler markers: SliceAssign.MainThread / ResetJob for 2A; CountJob / PrefixSum / ResetJob for 2B