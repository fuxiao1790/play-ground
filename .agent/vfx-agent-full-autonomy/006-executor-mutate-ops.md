# 006 - Executor Mutation and Commit Semantics

## Goal

Add graph-scoped method calls and writes with honest partial-failure behavior,
automatic invalidation, transient cleanup, and save only after full success.

## Dependencies

005.

## Operations

- `set`: field/property assignment after policy, writability, and expected-type
  validation.
- `call`: instance method invocation. If overloads share a name,
  `parameterTypes` is required and must match one exact stable signature.
  Reject generic methods, delegates, pointer parameters, and `ref`/`out` in
  first implementation.
- `create`: constructors only for policy-approved transient CLR/Unity value
  types; exact constructor signature required when ambiguous.
- `create_scriptable_object`: only concrete policy-approved `VFXModel` types.
  Prefer existing `VFXLibrary`/`CreateNode` path for catalogue nodes and
  variants; this operation is fallback, not a second type catalogue.

No `mark_dirty` or `save` operation exists. Track whether any `set` or `call`
started. In `finally`, conservatively invalidate guarded graph after possible
mutation so failure does not leave caches inconsistent. On full success, save
only when request-level `saveOnSuccess` is true by reusing `SaveGraph`.

Track created ScriptableObjects. Destroy any still-unattached transient object
on completion/failure; never destroy a model now owned by guarded graph.

## Failure Semantics

This is a sequential batch, not a transaction. Unity VFX model mutation does
not participate reliably in standard `Undo` merely because an Undo group is
opened. Do not add decorative Undo grouping.

On failure:

- stop at failing operation;
- never save;
- return completed results, `failedOperation`, `saved:false`, and
  `mayHaveMutated:true` when a mutation-capable operation began;
- caller must re-read graph and errors before deciding how to recover.

Failure during final invalidation or save uses `failureStage:"postprocess"` or
`"save"` and `failedOperation:null`. `saved:false` means unconfirmed, not proof
that no bytes changed; caller verifies graph state and disk state.

## Acceptance Criteria

- `call` can link and unlink compatible context flow edges through installed
  `VFXContext` methods; graph snapshot observes exact slot indices.
- `set` and method args accept local aliases, arrays, curves, and asset refs.
- Cross-graph target fails before invocation.
- Failure after earlier mutation leaves change observable in memory, never
  saves, and reports `mayHaveMutated:true`.
- Successful `saveOnSuccess:true` persists graph; false leaves explicit save to
  normal workflow.
- No Undo/rollback claim appears in code or docs.

## Scope

Large and highest-risk task.
