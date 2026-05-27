/// <summary>
/// Adapter-side callback implemented by gameplay targets that receive projectile hits.
/// </summary>
public interface Target
{
	void OnHit(in ProjectileHitContext hit);
}
