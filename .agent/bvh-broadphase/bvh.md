# Configurable Wide Bounding-Circle BVH for Unity DOTS / Burst

## 1. Purpose

This document specifies a collision-only broadphase BVH designed for a Unity DOTS workload with a very large number of moving ECS entities.

The design intentionally does **not** use Unity Physics / `CollisionWorld`.

The goals are:

- Keep movement fully owned by the existing ECS systems.
- Use a custom hierarchy only for collision candidate filtering.
- Use **bounding circles** instead of AABBs throughout the hierarchy.
- Use a **compile-time configurable branching factor** so BVH4, BVH8, and other fixed widths can be benchmarked easily.
- Control the branching factor from one `const int ChildCount`.
- Perform the geometric test against all children of the current node as one SIMD batch when the configured width maps cleanly to the target ISA.
- Keep tree traversal itself scalar.
- Continue descending until the hierarchy resolves to a single object.
- Run the final exact source-vs-object collision test as scalar code.
- Keep node data contiguous and structure-of-arrays oriented.
- Avoid pointer-heavy object graphs, managed allocations, and per-query temporary containers.
- Be compatible with Burst jobs and native containers.

This is a **broadphase acceleration structure**, not a general-purpose physics engine.

---

# 2. Compile-Time Branching Factor

The branching factor must be controlled from one compile-time constant:

```csharp
public static class BvhConfig
{
    public const int ChildCount = 8;
}
```

For testing, change only this value and recompile:

```csharp
public const int ChildCount = 4;
```

or:

```csharp
public const int ChildCount = 8;
```

The implementation must not scatter hard-coded branching values through the tree code. `BvhConfig.ChildCount` must determine:

- children per node
- child-bound storage
- child-reference storage
- child-kind storage
- active-lane mask width
- construction grouping size
- parent grouping size
- traversal loops
- validation loops
- ideal-depth calculations

The value is intentionally a compile-time constant rather than a runtime setting. Burst should be allowed to specialize and unroll the node-test code for the selected width. Benchmark BVH4 and BVH8 as separate compiled configurations rather than dynamically switching widths inside one traversal kernel.

---

# 3. High-Level Structure

The hierarchy is a fixed-width bounding volume hierarchy whose branching factor is `BvhConfig.ChildCount`:

```text
                             Node
          ┌────────┬────────┬────────┬────────┐
          │        │        │        │        │
        child0   child1   child2   ...   childN-1
          │
          ▼
        Node
     ┌────┼────┐
    ...  ...  ...
          │
          ▼
        object
```

Each internal node contains up to `BvhConfig.ChildCount` children.

Each child is represented by:

- bounding-circle center X
- bounding-circle center Y
- bounding-circle radius
- child reference
- child type

A child reference points to either:

1. another BVH node, or
2. one final object

The geometric overlap test for all configured children is performed simultaneously when the selected width maps to SIMD.

Traversal to a selected child remains scalar.

---

# 4. Core Design Decision

## 3.1 SIMD happens across the children of one node

Do **not** SIMD multiple unrelated traversal branches together.

For one query source:

```text
scalar:
    choose current node

SIMD:
    test current node's ChildCount children

scalar:
    determine which children survived
    visit those children one at a time

SIMD:
    test each selected child's ChildCount children

repeat
```

This avoids packet-traversal divergence.

Every node is independently designed around the configured compile-time width.

---

# 5. Bounding Volume

Every internal child uses a bounding circle.

A bounding circle is:

```text
center = (x, y)
radius = r
```

A source collision shape must provide a broadphase circle:

```text
source center = (sx, sy)
source broadphase radius = sr
```

The broadphase overlap test is:

```text
dx = childX - sx
dy = childY - sy

combinedRadius = childRadius + sr

overlap =
    dx * dx +
    dy * dy
    <=
    combinedRadius * combinedRadius
```

No square root is required.

---

# 6. SIMD Node Layout

The node must use a structure-of-arrays layout.

Do not store:

```csharp
struct Child
{
    float2 Center;
    float Radius;
    int ChildIndex;
}

struct Node
{
    Child C0;
    Child C1;
    ...
}
```

That layout is less convenient for SIMD.

Instead, conceptually store:

```text
centerX:
[x0 x1 x2 ... xN-1]

centerY:
[y0 y1 y2 ... yN-1]

radius:
[r0 r1 r2 ... rN-1]

reference:
[c0 c1 c2 ... cN-1]

type:
[t0 t1 t2 ... tN-1]
```

The first three arrays are the hot traversal data.

The references and type metadata are only needed after the SIMD overlap mask is produced.

---

# 7. Recommended Physical Node Representation

Use the same compile-time `BvhConfig.ChildCount` directly in the physical node storage.

A straightforward implementation is an unsafe fixed-buffer struct:

```csharp
public static class BvhConfig
{
    public const int ChildCount = 8;
}

public unsafe struct BvhNode
{
    // Hot SIMD data. All lanes are contiguous.
    public fixed float CenterX[BvhConfig.ChildCount];
    public fixed float CenterY[BvhConfig.ChildCount];
    public fixed float Radius[BvhConfig.ChildCount];

    // Cold traversal metadata.
    public fixed int ChildReference[BvhConfig.ChildCount];
    public fixed byte ChildKind[BvhConfig.ChildCount];

    // Bit i == 1 means lane i is valid.
    public uint ActiveMask;
}
```

For the intended test range, require:

```csharp
BvhConfig.ChildCount <= 32
```

so a `uint` can represent the active lanes. If widths above 32 are ever tested, change only the mask representation; the tree algorithm remains the same.

There is no runtime branching-factor field on a node. `BvhConfig.ChildCount` is the branching factor for the entire compiled implementation.

Construction and traversal loops must use the constant directly:

```csharp
for (int i = 0; i < BvhConfig.ChildCount; i++)
{
    // build / validate / initialize lane i
}
```

Because `ChildCount` is a compile-time constant, Burst can specialize or unroll these loops. Verify the resulting machine code in Burst Inspector rather than assuming vectorization.

The required memory property is:

> The `ChildCount` X values, Y values, and radius values for a node are stored contiguously so the node test can load them as packed vectors without gathers.

---

# 8. Child Kind

Each child must be either:

```text
InternalNode
Object
Unused
```

Recommended encoding:

```csharp
public enum BvhChildKind : byte
{
    Unused = 0,
    Node = 1,
    Object = 2
}
```

Unused lanes must never produce a hit.

They can be disabled either by:

- an explicit active-lane bitmask, or
- setting their radius to a value that can never pass and masking them afterward

An explicit lane mask is preferred.

Example:

```text
ChildCount = 5

active mask:
00011111
```

---

# 9. Child Reference

The child reference is an integer index.

For a node child:

```text
ChildReference = index into Nodes[]
```

For an object child:

```text
ChildReference = index into BroadphaseObjects[]
```

Do not store pointers.

Use contiguous arrays:

```csharp
NativeArray<BvhNode> Nodes;
NativeArray<BroadphaseObject> Objects;
```

or persistent native containers backed by contiguous memory.

This gives:

- predictable cache behavior
- Burst compatibility
- relocatable storage
- cheap serialization/debugging
- no managed pointer chasing

---

# 10. Object Representation

The hierarchy only needs the information required to:

1. generate bounds
2. identify the ECS entity / collision object
3. run the final exact collision test

A broadphase object can look like:

```csharp
public struct BroadphaseObject
{
    public float2 Center;

    // Bounding radius used for BVH construction.
    public float BroadphaseRadius;

    // Index or entity used by the narrowphase.
    public Entity Entity;

    // Optional type or shape index.
    public int ShapeIndex;
}
```

If final collision shapes use different formats, keep those in separate tightly packed arrays and use `ShapeIndex` / type information to locate them.

Do not inflate the BVH object structure with unrelated gameplay data.

---

# 11. Query Representation

A query source needs:

```csharp
public struct BroadphaseQuery
{
    public float2 Center;
    public float Radius;
}
```

The query radius must conservatively enclose the source's exact collision shape.

Examples:

### Circle source

```text
query center = circle center
query radius = circle radius
```

### Rectangle source

Use a conservative enclosing circle:

```text
radius = sqrt(halfWidth² + halfHeight²)
```

### Capsule source

Use a conservative circle that contains the capsule.

The broadphase is allowed to return false positives.

It must never return false negatives.

---

# 12. Node Traversal Test

For one source query:

```text
sx = source.Center.x
sy = source.Center.y
sr = source.Radius
```

For a node:

```text
childX = [x0 ... x7]
childY = [y0 ... y7]
childR = [r0 ... r7]
```

SIMD computation:

```text
dx = childX - broadcast(sx)
dy = childY - broadcast(sy)

r = childR + broadcast(sr)

distanceSquared =
    dx * dx +
    dy * dy

radiusSquared =
    r * r

hit =
    distanceSquared <= radiusSquared
```

Then apply:

```text
hit &= activeLaneMask
```

The result is a logical hit mask with one bit per configured child lane.

Example:

```text
children:
0 1 2 3 4 5 6 7

hit:
1 0 1 0 0 1 0 0

mask:
00100101
```

---

# 13. Traversal Is Scalar

Once the SIMD mask has been produced, process the surviving children one at a time.

Pseudo-code:

```csharp
while (mask != 0)
{
    int lane = ExtractLowestSetBitIndex(mask);

    int childRef = node.ChildReference[lane];
    BvhChildKind kind = node.ChildKind[lane];

    if (kind == BvhChildKind.Node)
    {
        Push(childRef);
    }
    else if (kind == BvhChildKind.Object)
    {
        ExactCollision(source, Objects[childRef]);
    }

    mask &= mask - 1;
}
```

The important point is:

> Do not attempt to SIMD the transition from one tree node to another.

The SIMD unit is always the `BvhConfig.ChildCount` children belonging to the currently selected node.

---

# 14. Traversal Stack

Do not allocate a stack per query.

Use a fixed-size local stack.

A wide BVH hierarchy is shallow.

For `N` objects, ideal depth is approximately:

```text
depth ≈ ceil(log base ChildCount of N)
```

Examples:

```text
1,000 objects       ~4 levels
10,000 objects      ~5 levels
100,000 objects     ~6 levels
1,000,000 objects   ~7 levels
```

A fixed stack of 32 or 64 node references should therefore be far larger than necessary for a well-formed tree.

Example:

```csharp
unsafe struct TraversalStack
{
    public fixed int NodeIndices[64];
    public int Count;
}
```

If unsafe code is undesirable, use a Burst-compatible fixed-list type whose capacity is known at compile time.

Do not use:

```csharp
NativeList<int>
```

per query.

That would add unnecessary allocation / bookkeeping overhead.

---

# 15. Recommended Traversal Order

Depth-first traversal is recommended.

Pseudo-code:

```text
push root

while stack not empty:
    node = pop

    mask = SIMD test node's ChildCount children

    for each set bit in mask:
        if child is object:
            scalar exact collision
        else:
            push child node
```

Depth-first traversal has good practical properties:

- tiny fixed stack
- simple implementation
- good cache locality
- no large traversal queue
- easy Burst job integration

---

# 16. Optional Near-First Traversal

If early exit is useful, such as:

```text
find any collision
```

or:

```text
find nearest collision
```

children may be processed approximately nearest first.

This requires extra work and is not part of the baseline implementation.

For "find all overlaps", simple depth-first traversal is preferred.

---

# 17. Leaf Policy

The baseline design should descend until a child references a **single object**.

That means no multi-object leaf is required.

The node itself already provides the SIMD grouping:

```text
Node
    ChildCount child bounds tested together
```

If the selected child is another node:

```text
descend scalar
```

If the selected child is an object:

```text
run exact scalar collision test
```

This matches the intended design:

> SIMD for hierarchy filtering, scalar for one final object.

---

# 18. Exact Narrowphase

Once a child is an object, do not use the bounding circle as the final collision result unless the object's real collision shape is itself exactly that circle.

Run the actual scalar test.

Examples:

```text
source circle vs target circle
source circle vs target rectangle
source capsule vs target circle
source rectangle vs target rectangle
```

False positives from broadphase are expected.

The broadphase must only guarantee:

```text
if exact collision exists
    hierarchy must not prune the object
```

---

# 19. Tree Construction

The tree construction algorithm must optimize for spatially compact groups of `BvhConfig.ChildCount` children.

The exact construction method can be improved later, but the initial implementation should use a deterministic top-down partitioning algorithm.

Recommended baseline:

1. Collect all objects.
2. Compute each object's broadphase circle.
3. Compute a spatial key from its center.
4. Sort objects by that spatial key.
5. Group nearby objects into groups of at most `BvhConfig.ChildCount`.
6. Build parent circles around each group.
7. Repeat grouping the resulting nodes until only one root remains.

A Morton-code / Z-order spatial key is recommended as the first implementation because it is:

- simple
- deterministic
- parallelizable
- cache-friendly
- much cheaper to build than a full SAH optimizer

---

# 20. Morton Key Construction

For each object:

```text
world position
    ↓
normalize into broadphase world bounds
    ↓
quantize X/Y
    ↓
interleave X/Y bits
    ↓
Morton code
```

Then sort objects by Morton code.

Nearby Morton codes tend to correspond to nearby positions.

The sorted sequence can then be grouped:

```text
For `ChildCount = 8`, for example:

objects:
0..7      -> parent node child group
8..15     -> parent node child group
16..23    -> parent node child group
...
```

This is not theoretically optimal, but it is an excellent baseline for a frequently rebuilt real-time BVH.

---

# 21. Bottom-Up Group Construction

After sorting objects:

## Level 0

Each object is represented as a child bound.

Group them in sets of up to `BvhConfig.ChildCount`:

```text
For `ChildCount = 8`, for example:

objects 0-7
    ↓
node 0

objects 8-15
    ↓
node 1

objects 16-23
    ↓
node 2
```

## Level 1

Compute one bounding circle per node.

Then group those nodes in sets of up to `BvhConfig.ChildCount`:

```text
For `ChildCount = 8`, for example:

node 0..7
    ↓
parent node
```

Repeat until only one node remains.

That final node is the root.

---

# 22. Bounding Circle Construction

For each group of child circles, build one parent circle that contains all children.

The first implementation should prioritize low build cost over mathematically minimal circles.

Recommended conservative method:

1. Compute the AABB of all child circles:

```text
minX = min(childX - childRadius)
maxX = max(childX + childRadius)

minY = min(childY - childRadius)
maxY = max(childY + childRadius)
```

2. Set parent center to the AABB center:

```text
cx = (minX + maxX) * 0.5
cy = (minY + maxY) * 0.5
```

3. Compute required radius for each child:

```text
requiredRadius_i =
    distance(parentCenter, childCenter_i)
    + childRadius_i
```

4. Parent radius is:

```text
max(requiredRadius_i)
```

This gives a conservative enclosing circle.

It is cheap and robust.

Do not initially implement a minimum-enclosing-circle algorithm.

---

# 23. Why Parent Circle Construction Uses a Square Root

Traversal must avoid square roots.

Tree construction does not have the same requirement.

For parent construction:

```text
distance =
    sqrt(dx² + dy²)
```

is acceptable because:

- it happens during build/refit
- it happens once per child group
- traversal happens vastly more often than construction arithmetic

If the tree is rebuilt every frame and this becomes measurable, optimize later.

Correctness and bound quality come first.

---

# 24. Rebuild vs Refit

There are two approaches for moving entities.

## Full rebuild

```text
current object positions
    ↓
recompute Morton codes
    ↓
sort
    ↓
rebuild hierarchy
```

Advantages:

- restores spatial quality
- simple
- predictable
- naturally parallel

Disadvantages:

- sorting cost

## Refit

Keep topology unchanged:

```text
update object bounds
    ↓
recompute leaf-parent bounds
    ↓
propagate bounds upward
```

Advantages:

- cheaper than sorting/rebuilding

Disadvantages:

- hierarchy quality degrades as objects move far from their original groups

---

# 25. Recommended Update Strategy

Start with a **full rebuild** benchmark.

Do not assume refit is necessary.

For a large number of highly mobile projectiles, a refitted topology may degrade quickly enough that traversal cost outweighs the saved build cost.

Benchmark:

```text
A. full rebuild every collision tick

B. refit every tick
   rebuild every N ticks

C. refit every tick
   rebuild when hierarchy-quality metric exceeds threshold
```

The simplest implementation should be A first.

---

# 26. Hierarchy Quality Metric

If refitting is added later, track how loose the nodes become.

A simple metric is:

```text
parentCircleArea /
sum(childCircleAreas)
```

or simply:

```text
parentRadius²
```

relative to the distribution of child bounds.

A rapid increase means the group's children have spread apart and should be repartitioned.

The exact rebuild heuristic should be benchmark-driven.

---

# 27. Double Buffering

If the hierarchy is rebuilt while another job still queries the previous hierarchy, use double buffering:

```text
Tree A
    currently being queried

Tree B
    being built

after dependency completes:
    swap A and B
```

This avoids in-place mutation hazards.

Example persistent state:

```text
NodesA
ObjectsA

NodesB
ObjectsB

CurrentTreeIndex
```

---

# 28. ECS Integration

The BVH should live in its own system-owned native memory.

Do not create one BVH entity per node.

Keep the hierarchy as contiguous native arrays.

Example:

```csharp
public partial struct CollisionBroadphaseSystem : ISystem
{
    NativeList<BvhNode> _nodesA;
    NativeList<BvhNode> _nodesB;

    NativeList<BroadphaseObject> _objectsA;
    NativeList<BroadphaseObject> _objectsB;
}
```

The ECS entities remain the gameplay source of truth.

The BVH is an acceleration structure derived from them.

---

# 29. Data Flow

Recommended collision tick:

```text
ECS movement systems
        ↓
positions are final for this collision tick
        ↓
extract / write broadphase object data
        ↓
build or refit BVH
        ↓
schedule collision query jobs
        ↓
each source independently traverses BVH
        ↓
scalar exact tests
        ↓
write hit events
```

The tree must never be queried while its node data is being mutated.

Use job dependencies accordingly.

---

# 30. Parallel Querying

Each source can traverse the same read-only BVH independently.

This is ideal for:

```csharp
IJobEntity
```

or:

```csharp
IJobParallelFor
```

Conceptually:

```text
worker 0:
    source 0
    source 8
    source 16
    ...

worker 1:
    source 1
    source 9
    source 17
    ...
```

All workers read:

```text
Nodes
Objects
```

No locks are required for the tree.

---

# 31. Query Job Shape

Pseudo-code:

```csharp
[BurstCompile]
public struct QueryJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<BvhNode> Nodes;

    [ReadOnly]
    public NativeArray<BroadphaseObject> Objects;

    [ReadOnly]
    public NativeArray<QuerySource> Sources;

    public void Execute(int index)
    {
        QuerySource source = Sources[index];

        Traverse(source);
    }
}
```

Traversal must use stack memory / fixed local storage only.

---

# 32. Traversal Pseudo-Code

```csharp
void Traverse(in QuerySource source)
{
    FixedStack stack = default;

    stack.Push(RootNodeIndex);

    while (stack.Count > 0)
    {
        int nodeIndex = stack.Pop();

        ref readonly BvhNode node = ref Nodes[nodeIndex];

        byte mask = TestNode8(
            source.Center,
            source.BroadphaseRadius,
            node);

        while (mask != 0)
        {
            int lane = TrailingZeroCount(mask);

            int childRef = GetChildReference(node, lane);
            BvhChildKind kind = GetChildKind(node, lane);

            if (kind == BvhChildKind.Node)
            {
                stack.Push(childRef);
            }
            else if (kind == BvhChildKind.Object)
            {
                TestExactCollision(
                    source,
                    Objects[childRef]);
            }

            mask &= (byte)(mask - 1);
        }
    }
}
```

---

# 33. SIMD Test Pseudo-Code

Conceptually:

```csharp
mask = Test8Circles(
    sourceX,
    sourceY,
    sourceRadius,
    childX0..7,
    childY0..7,
    childRadius0..7);
```

Equivalent vector math:

```text
dx = X8 - sourceX
dy = Y8 - sourceY

rr = R8 + sourceRadius

hit =
    dx*dx + dy*dy
    <=
    rr*rr
```

Then convert the comparison result to an 8-bit mask.

The exact Burst implementation should be selected by examining generated assembly.

---

# 34. Burst Implementation Rule

Do not assume that writing:

```csharp
for (int i = 0; i < 8; i++)
```

guarantees AVX2.

After the basic implementation works:

1. Burst compile the traversal kernel.
2. Open Burst Inspector.
3. Verify that the node test uses packed SIMD instructions.
4. Check for:
   - unnecessary scalar loads
   - gathers
   - lane-by-lane branches
   - spills
   - stack traffic
5. Adjust physical node layout accordingly.

The design requirement is semantic:

> one node's 8 circles must map cleanly onto SIMD-wide processing.

The exact C# representation is secondary.

---

# 35. Alignment

Node arrays should be naturally aligned.

Try to keep hot node data in cache-friendly fixed-size blocks.

The hot traversal data per node is:

```text
8 centerX floats = 32 bytes
8 centerY floats = 32 bytes
8 radius floats  = 32 bytes
```

Total:

```text
96 bytes
```

Metadata adds more.

Consider separating hot and cold data:

```csharp
NativeArray<BvhNodeBounds8> Bounds;
NativeArray<BvhNodeMeta8> Meta;
```

where:

```csharp
struct BvhNodeBounds8
{
    // X[8], Y[8], R[8]
}

struct BvhNodeMeta8
{
    // references, kinds, active mask
}
```

This can reduce cache traffic when a node produces no hits.

---

# 36. Preferred Hot/Cold Split

Recommended layout:

```text
Bounds array:
NodeBounds8[]
    X[8]
    Y[8]
    R[8]

Metadata array:
NodeMeta8[]
    ChildReference[8]
    ChildKind[8]
    ActiveMask
```

Traversal:

```text
load bounds
    ↓
SIMD test
    ↓
mask == 0 ?
    yes:
        skip metadata entirely

    no:
        load metadata
        process surviving children
```

This is preferable if many queried nodes fail completely.

---

# 37. Root Handling

Store:

```csharp
int RootNodeIndex;
```

The root itself should always be an internal node unless the entire tree contains very few objects.

For very small object counts, two options are valid:

### Option A

Always construct one root node and place objects directly in its child lanes.

Preferred.

### Option B

Special-case a tree containing one object.

Not recommended unless profiling proves useful.

Keep traversal uniform.

---

# 38. Empty Tree

If there are no broadphase objects:

```text
RootNodeIndex = -1
```

Query jobs immediately return.

---

# 39. Fewer Than `ChildCount` Children

A node may contain from 1 through `BvhConfig.ChildCount` active children.

Use:

```text
ActiveMask
```

Example:

```text
5 valid children:

00011111
```

The SIMD operation may still process all configured lanes.

Afterward:

```text
mask &= ActiveMask
```

Do not branch over `ChildCount` inside the geometric kernel.

---

# 40. Collision Layers

If collision-category filtering is required, it can be incorporated into the hierarchy.

Each child can contain a combined category mask representing all descendants.

Example:

```csharp
uint ChildLayerMask[8];
```

For a query:

```text
queryCollidesWithMask
```

SIMD/spatial result:

```text
spatialMask
```

Layer result:

```text
layerMask
```

Final traversal mask:

```text
mask = spatialMask & layerMask & activeMask
```

At an internal node, the child's layer mask must be the OR of all descendant object layers.

This lets entire branches be skipped.

---

# 41. Self-Collision Filtering

If the source can exist inside the same tree, an object may encounter itself.

At object level:

```csharp
if (candidate.Entity == source.Entity)
    return;
```

Do this only after reaching the object.

Do not complicate internal hierarchy traversal to eliminate self-overlap.

---

# 42. Duplicate Pair Handling

If every object queries the same tree, collision pairs can appear twice:

```text
A queries B
B queries A
```

If only one event is desired, use a deterministic ordering rule at the final object stage:

```text
if sourceObjectIndex >= candidateObjectIndex:
    skip
```

This is only appropriate when both objects belong to the same symmetric collision set.

For projectile-vs-target systems where only projectiles query targets, this is unnecessary.

---

# 43. Separate Trees by Collision Domain

If there are very different object sets, prefer separate trees.

Example:

```text
Target BVH
    enemies / players

Projectile BVH
    only if projectile-vs-projectile is needed

Environment BVH
    static collision geometry
```

Do not automatically put every collidable entity into one enormous tree.

A query should traverse the smallest relevant hierarchy.

---

# 44. Static and Dynamic Data

Static objects can use a tree that is built once.

Dynamic objects use a rebuilt/refitted tree.

Example:

```text
StaticTree
    walls
    terrain
    stationary hazards

DynamicTargetTree
    enemies
    moving targets
```

A source may query both trees independently.

---

# 45. Numerical Rules

All radii must satisfy:

```text
radius >= 0
```

All node circles must conservatively contain their descendants.

Use squared distance during traversal.

Never use:

```text
sqrt(distanceSquared)
```

during overlap checks.

Use:

```text
distanceSquared <= combinedRadiusSquared
```

---

# 46. Floating-Point Margin

To avoid false negatives caused by floating-point error at exact contact boundaries, consider:

```text
combinedRadius += epsilon
```

or:

```text
distanceSquared <= combinedRadiusSquared + epsilon
```

Keep epsilon small and consistent with world scale.

Do not use an excessively large tolerance because it increases false positives.

---

# 47. Large Objects

Very large objects can produce loose parent bounds.

If a few objects are much larger than everything else, consider:

- placing them in a separate tree
- keeping them near the root
- querying them separately

Do not let a single enormous radius destroy pruning quality for an otherwise compact group.

---

# 48. Spatial Distribution Considerations

Bounding circles work best for approximately isotropic groups.

They are less efficient for long thin distributions:

```text
o o o o o o o o o o o o
```

because the enclosing circle contains large empty areas.

The Morton grouping step should therefore preserve locality as much as possible.

If profiling shows poor pruning for elongated groups, possible future improvements are:

- smarter 8-way clustering
- SAH-based grouping
- hybrid AABB/circle hierarchy
- separate trees by spatial region

Do not add these before the baseline is measured.

---

# 49. Build Parallelization

Potential build stages:

```text
Job 1:
    gather centers/radii

Job 2:
    calculate Morton keys

Job 3:
    sort by key

Job 4:
    build level 0 nodes

Job 5:
    build level 1

...

Query jobs
```

Some stages may be merged after profiling.

Tree construction must not introduce excessive scheduling overhead for small target counts.

---

# 50. Sorting

Sorting is likely to dominate a full rebuild for very large `N`.

Use an unmanaged/native sorting implementation.

The sorted data should keep:

```text
MortonKey
ObjectIndex
```

together.

Avoid copying entire gameplay components during sort.

---

# 51. Memory Reuse

All broadphase buffers should be persistent and reused.

Do not allocate every tick.

Use growth similar to:

```text
if required > capacity:
    grow capacity geometrically
```

Example:

```text
newCapacity =
    max(required, oldCapacity * 2)
```

Reuse:

- object arrays
- Morton key arrays
- temporary build arrays
- node arrays

---

# 52. No Per-Object Dynamic Allocation

Never create:

- managed objects
- `List<T>`
- individual native containers
- heap nodes
- per-query `NativeList`
- per-object collider objects

The entire design should operate over large flat arrays.

---

# 53. Cache Behavior

The query hot path should ideally touch:

```text
source data
node bounds
node metadata only for matching nodes
candidate narrowphase data
```

Avoid touching ECS archetype/chunk data randomly during traversal.

Copy or expose the minimal narrowphase data in collision-oriented arrays if random ECS component lookup becomes expensive.

---

# 54. Narrowphase Data Layout

If most candidates share one exact shape type, use SoA or compact arrays.

Example for circles:

```text
TargetX[]
TargetY[]
TargetRadius[]
TargetEntity[]
```

Even though the final test is scalar, this keeps candidate lookup cache-friendly.

If multiple shapes exist, group by collision type where practical.

---

# 55. Early Exit Variants

The baseline should support "find all".

Optional specialized query modes:

```text
AnyHit
FirstHit
AllHits
```

`AnyHit` may return immediately after the first exact collision.

Do not burden the baseline all-hits traversal with unnecessary nearest-distance bookkeeping.

---

# 56. Result Writing

The BVH itself must not decide how hits are stored.

The query job should emit into the existing collision event pipeline.

Possible choices:

- per-worker native buckets
- `NativeStream`
- thread-owned fixed/growable buffers
- other existing project-specific event containers

Avoid global atomic append if the current architecture already uses worker-local buckets.

---

# 57. Validation

Before optimizing, validate the BVH against brute force.

For randomized scenes:

```text
for every source:
    brute-force all candidates
    BVH query candidates

assert:
    every real brute-force collision
    is also returned by BVH
```

False positives are acceptable.

False negatives are bugs.

---

# 58. Debug Validation of Bounds

For every node child that references another node:

```text
parent child circle
    must contain
all circles in descendant subtree
```

For every node child that references an object:

```text
child circle
    must contain
object broadphase shape
```

Add expensive validation only in development/debug builds.

---

# 59. Performance Metrics

Measure at least:

## Build

```text
Morton key generation
sort time
node build time
total rebuild time
```

## Query

```text
average nodes visited per source
average internal child circles tested
average objects reaching narrowphase
exact collision tests per source
total query job time
```

## Tree quality

```text
average number of surviving children per visited node
```

For a good BVH, this should be much lower than `BvhConfig.ChildCount` in most scenes.

---

# 60. SIMD Verification Metrics

Use Burst Inspector and CPU profiling to verify:

- 8-wide packed float operations where expected
- no scalar per-lane geometric loop
- no unexpected gathers
- no excessive spills
- no unnecessary conversion between vector and scalar forms
- efficient mask generation

Do not infer SIMD solely from source code appearance.

---

# 61. BVH4 vs BVH8 Benchmark

The branching factor must be benchmarkable by changing only `BvhConfig.ChildCount` and recompiling.

Benchmark:

```text
BVH4
BVH8
```

BVH8 advantages:

- shallower tree
- natural AVX2 float width
- fewer scalar traversal transitions

BVH4 advantages:

- smaller nodes
- potentially tighter pruning
- lower cache footprint

Do not assume BVH8 wins on every CPU or scene distribution.

---

# 62. Initial Implementation Order

Implement in this order.

## Phase 1: correctness

1. `BroadphaseObject`
2. `BvhNode`
3. conservative parent-circle builder
4. simple sorted/grouped tree construction
5. scalar node test
6. scalar traversal
7. exact object collision
8. brute-force correctness validation

## Phase 2: SIMD

9. reorganize node bounds into SIMD-friendly SoA form
10. replace scalar 8-child test with vectorized version
11. inspect Burst assembly
12. tune node memory layout

## Phase 3: build performance

13. Morton codes
14. parallel key generation
15. optimized native sort
16. parallel node-level construction
17. persistent buffer reuse

## Phase 4: update strategy

18. benchmark full rebuild
19. add refit if needed
20. benchmark periodic rebuild + refit

Do not optimize tree construction before traversal correctness is proven.

---

# 63. Reference Algorithm

The final intended algorithm is:

```text
MOVEMENT SYSTEMS
    |
    v
Current ECS positions
    |
    v
Build/update collision objects
    |
    v
Spatial sort
    |
    v
Build BVH8
    |
    v
For each source in parallel
    |
    v
push root
    |
    v
+-------------------------------+
| scalar pop current node       |
|                               |
| SIMD test ChildCount child circles     |
|                               |
| produce child hit mask        |
|                               |
| for each surviving child      |
|     scalar dispatch:          |
|                               |
|     internal node -> push     |
|                               |
|     object -> exact scalar    |
|               collision      |
+-------------------------------+
    |
    v
emit hit events
```

---

# 64. Core Principle

The implementation should preserve this rule:

> **The tree traversal is scalar, but every internal node processes its configured set of child bounding-volume tests as one fixed-width SIMD batch where the target ISA supports that width cleanly.**

For example, with `BvhConfig.ChildCount = 8` on an AVX2-capable CPU:

```text
one selected node
        ↓
ChildCount child circles
        ↓
one fixed-width SIMD collision batch
        ↓
logical overlap mask with one bit per child
        ↓
scalar selection of surviving children
        ↓
repeat
```

At the final object:

```text
one source
vs
one object
```

run the exact collision test scalar.

This is the intended architecture.
