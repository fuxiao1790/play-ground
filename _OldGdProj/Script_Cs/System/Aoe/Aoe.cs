/// <summary>
/// Adapter-side callback implemented by gameplay AOE owners that react to hits.
/// </summary>
public interface Aoe
{
	void OnHit(in AoeHitContext hit);
}
