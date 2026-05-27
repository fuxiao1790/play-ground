/// <summary>
/// Lightweight runtime handle that lets packed projectile data refer back to adapter-owned entities.
/// </summary>
public readonly struct EntityHandle : System.IEquatable<EntityHandle>
{
	public static readonly EntityHandle Invalid = new(0);

	public int Id { get; }

	public bool IsValid => Id > 0;

	public EntityHandle(int id)
	{
		Id = id;
	}

	public bool Equals(EntityHandle other)
	{
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is EntityHandle other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}

	public static bool operator ==(EntityHandle left, EntityHandle right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(EntityHandle left, EntityHandle right)
	{
		return !left.Equals(right);
	}
}
