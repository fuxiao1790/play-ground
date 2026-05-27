/// <summary>
/// Adapter-side callback implemented by gameplay projectile owners that react to hits.
/// </summary>
public interface Projectile
{
	void OnHit(in ProjectileHitContext hit);
}
