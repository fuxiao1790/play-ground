You are working on a codebase refactor where the primary goal is **sound structure**, not immediate logic perfection.

Your priority is to design and implement a structure that will survive future changes. Small logic bugs are acceptable and easy to fix later. Bad structure is not acceptable, because it causes another rewrite.

Follow these principles:

1. **Structure over correctness**

   * Do not optimize for making the current behavior barely work.
   * Optimize for creating clean boundaries, clear ownership, predictable data flow, and maintainable phases.
   * Prefer an implementation with a few small logic mistakes over one that preserves messy structure.

2. **Do not patch around structural problems**

   * If the existing design forces awkward coupling, duplicate state, fragile ordering, hidden dependencies, or unclear ownership, stop and restructure it.
   * Do not add defensive glue just to make the current system safe.
   * Avoid “temporary” compatibility layers unless they are explicitly part of the migration plan.

3. **Make ownership obvious**

   * Every piece of data should have a clear owner.
   * Every system should have a clear responsibility.
   * Avoid systems that both produce, transform, consume, and clean up the same kind of data.
   * Avoid bidirectional dependencies unless absolutely necessary.

4. **Enforce phase boundaries**

   * Separate collection, expansion, simulation, aggregation, dispatch, rendering, and cleanup where applicable.
   * Do not allow one phase to reach backward or forward into another phase’s responsibilities.
   * Prefer explicit intermediate data/events over hidden side effects.

5. **Design for future fixes**

   * Assume that logic details will change.
   * Make those changes localized.
   * A future developer should be able to fix formulas, conditions, edge cases, or ordering rules without changing the architecture.

6. **Prefer explicit constraints**

   * If a system relies on ordering, lifetime, single-writer ownership, fixed-depth nesting, disabled-entity reuse, or no structural changes during a phase, document that directly in code comments or task notes.
   * Do not leave structural assumptions implicit.

7. **Reject misleading safety**

   * Do not add excessive null checks, fallback paths, duplicated validation, or defensive copying if they hide unclear ownership.
   * Safety should come from structure first, not scattered guards.

8. **When choosing between two implementations**

   * Pick the one with clearer data flow.
   * Pick the one with fewer responsibilities per system.
   * Pick the one that makes future changes smaller.
   * Pick the one that exposes architectural mistakes sooner.
   * Do not pick the one that merely has fewer immediate compile/runtime errors.

9. **Implementation expectations**

   * It is acceptable to leave TODOs for small logic details.
   * It is acceptable to make conservative assumptions and document them.
   * It is not acceptable to leave structural decisions undecided.
   * It is not acceptable to preserve bad structure because fixing it is inconvenient.

10. **Before coding each task**

* Identify the structural role of the files being changed.
* Identify ownership boundaries.
* Identify data flow between systems.
* Identify phase/order assumptions.
* Then implement the change in a way that strengthens those boundaries.

Final goal: after this refactor, the codebase should have a shape where fixing behavior is straightforward. The success criterion is not “everything works perfectly today.” The success criterion is “the architecture is now correct enough that future logic fixes do not require another rewrite.”
