using Godot;

/// <summary>
/// Immutable damage payload captured before combat effect spawn and forwarded through hit events.
/// </summary>
public sealed class DamageSnapshot
{
	public static readonly DamageSnapshot Empty = new(System.Array.Empty<DamageComponent>());

	public readonly DamageComponent[] Components;

	public DamageSnapshot(DamageComponent[] components)
	{
		Components = components;
	}

	public static DamageSnapshot Single(float amount)
	{
		return new DamageSnapshot(new[]
		{
			new DamageComponent(amount, DamageTypeRef.Default)
		});
	}

	public int TotalWholeAmountOrDefault(int fallbackAmount)
	{
		if (Components.Length == 0)
		{
			return fallbackAmount;
		}

		float total = 0.0f;
		for (int i = 0; i < Components.Length; i++)
		{
			total += Components[i].Amount;
		}

		return Mathf.Max(1, Mathf.RoundToInt(total));
	}
}

/// <summary>
/// One typed damage amount inside a damage snapshot.
/// </summary>
public readonly record struct DamageComponent(
	float Amount,
	DamageTypeRef Type);

/// <summary>
/// Stable damage type identity plus optional rules for future interpretation.
/// </summary>
public readonly record struct DamageTypeRef(
	StringName Id,
	DamageTypeRules? Rules)
{
	public static readonly DamageTypeRef Default = new(new StringName("default"), null);
}

/// <summary>
/// Placeholder for richer per-damage-type behavior outside the combat runtimes.
/// </summary>
public sealed class DamageTypeRules
{
}
