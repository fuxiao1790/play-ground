# 003 — Update EditMode test that depends on removed TryStage API

## Scope

`Assets/Tests/EditMode/CombatVfxRootRegistrationTests.cs`

## Change

Replace `StagingBeyondInitialCapacity_GrowsBuffersWithoutDroppingRequests` (which calls the now-deleted
`TryStage` in a loop) with a test that exercises `EnsureBufferCapacity` directly:

```csharp
[Test]
public void EnsureBufferCapacity_BeyondInitialCapacity_GrowsWithoutShrinking()
{
    var res = new AoeVfxTypeResources { BufferCapacity = AoeVfxTypeResources.InitialBufferCapacity };
    try
    {
        int count = AoeVfxTypeResources.InitialBufferCapacity + 1;
        res.EnsureBufferCapacity(count);
        Assert.That(res.BufferCapacity, Is.GreaterThanOrEqualTo(count));
    }
    finally
    {
        res.Dispose();
    }
}
```

## Acceptance criteria

- Test file compiles against the new `AoeVfxTypeResources` shape (no `Staging`/`TryStage` refs).
- `RegisterSameAsset_ReturnsSameIdAndCreatesOneChild` and `RegisterNullAsset_ReturnsZeroAndCreatesNoChild`
  are untouched and still pass.
- New test passes.

## Depends on

002 (needs the final `AoeVfxTypeResources` shape with `Staging`/`TryStage` removed).
