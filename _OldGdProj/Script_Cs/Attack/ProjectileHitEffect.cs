using Godot;

public abstract partial class ProjectileHitEffect : Node
{
	public virtual void Configure(Node2D owner)
	{
	}

	public abstract void Apply(in ProjectileHitContext hit);
}
