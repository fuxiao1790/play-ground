using Godot;
using System.Collections.Generic;

public static class ProjectileVolleyBuilder
{
	public readonly struct SpawnRequest
	{
		public readonly Vector2 Position;
		public readonly Vector2 Velocity;

		public SpawnRequest(Vector2 position, Vector2 velocity)
		{
			Position = position;
			Velocity = velocity;
		}
	}

	public static void Build(
		List<SpawnRequest> buffer,
		Vector2 ownerPosition,
		Vector2 aimWorldPosition,
		Vector2 forward,
		int projectileCount,
		float volleySpreadDegrees,
		float jitterDegrees,
		RandomNumberGenerator rng,
		float speed)
	{
		buffer.Clear();

		if (forward == Vector2.Zero)
		{
			return;
		}

		int shotCount = Mathf.Max(1, projectileCount);
		float totalSpreadRadians = Mathf.DegToRad(volleySpreadDegrees);
		float jitterRadians = Mathf.DegToRad(jitterDegrees);

		for (int i = 0; i < shotCount; i++)
		{
			float shotAngle = GetSpreadAngle(totalSpreadRadians, i, shotCount);
			if (jitterRadians > 0.0f)
			{
				shotAngle += rng.RandfRange(-jitterRadians * 0.5f, jitterRadians * 0.5f);
			}

			Vector2 shotForward = forward.Rotated(shotAngle);
			Vector2 spawnPosition = ownerPosition;
			Vector2 aimForward = (aimWorldPosition - spawnPosition).Normalized();
			if (aimForward == Vector2.Zero)
			{
				aimForward = shotForward;
			}

			buffer.Add(new SpawnRequest(
				spawnPosition,
				aimForward.Rotated(shotAngle) * speed));
		}
	}

	private static float GetSpreadAngle(float totalSpreadRadians, int shotIndex, int shotCount)
	{
		if (shotCount <= 1)
		{
			return 0.0f;
		}

		float startAngle = -totalSpreadRadians * 0.5f;
		float angleStep = totalSpreadRadians / (shotCount - 1);
		return startAngle + (angleStep * shotIndex);
	}
}
