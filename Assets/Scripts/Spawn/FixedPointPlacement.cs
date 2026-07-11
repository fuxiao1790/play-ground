using UnityEngine;

namespace PlayGround.Spawn
{
    [CreateAssetMenu(menuName = "Play Ground/Spawn/Fixed Point Placement")]
    public sealed class FixedPointPlacement : SpawnPlacement
    {
        public override bool TryResolve(in SpawnContext ctx, out Vector2 position)
        {
            position = default;
            if (ctx.Points == null || ctx.Points.Count == 0)
            {
                return false;
            }

            global::System.Random rng = ctx.Rng ?? new global::System.Random();
            int startIndex = rng.Next(0, ctx.Points.Count);
            for (int i = 0; i < ctx.Points.Count; i++)
            {
                SpawnPoint point = ctx.Points[(startIndex + i) % ctx.Points.Count];
                if (point == null)
                {
                    continue;
                }

                position = point.SamplePosition(rng);
                return true;
            }

            return false;
        }
    }
}
