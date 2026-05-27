using Godot;

/// <summary>
/// Optional homing steering system that updates projectile velocity from target snapshots.
/// </summary>
public sealed class Tracking
{
	private const float MinimumDirectionLengthSquared = 0.000001f;
	private const float MinimumForwardDot = 0.0f;

	// Reads: target arrays, Position, Velocity, TargetMask
	// Writes: Velocity, tracking cooldown/entity/index
	public void Apply(
		ProjectileStore projectiles,
		TargetStore targets,
		int startIndex,
		int endIndex,
		double delta)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			ApplyOne(projectiles, targets, i, delta);
		}
	}

	private static void ApplyOne(
		ProjectileStore projectiles,
		TargetStore targets,
		int projectileIndex,
		double delta)
	{
		if (!projectiles.TrackingEnabled[projectileIndex])
		{
			return;
		}

		float speed = projectiles.Velocity[projectileIndex].Length();
		if (speed <= 0.0001f)
		{
			return;
		}

		float deltaSeconds = (float)delta;
		Vector2 currentDirection = projectiles.Velocity[projectileIndex] / speed;
		projectiles.TrackingQueryCooldownRemaining[projectileIndex] = Mathf.Max(
			0.0f,
			projectiles.TrackingQueryCooldownRemaining[projectileIndex] - deltaSeconds);

		bool hasTrackedTarget = TryRefreshTrackedTarget(projectiles, targets, projectileIndex);
		if (projectiles.TrackingQueryCooldownRemaining[projectileIndex] <= 0.0f)
		{
			hasTrackedTarget = TryAcquireTrackedTarget(projectiles, targets, projectileIndex, currentDirection);
			projectiles.TrackingQueryCooldownRemaining[projectileIndex] =
				projectiles.TrackingQueryIntervalSeconds[projectileIndex];
		}
		else if (!hasTrackedTarget)
		{
			return;
		}

		int trackedTargetIndex = projectiles.TrackedTargetIndex[projectileIndex];
		if (trackedTargetIndex < 0 || trackedTargetIndex >= targets.Count)
		{
			return;
		}

		Vector2 toTarget = targets.Position[trackedTargetIndex] - projectiles.Position[projectileIndex];
		if (toTarget.LengthSquared() <= MinimumDirectionLengthSquared)
		{
			return;
		}

		Vector2 desiredDirection = toTarget.Normalized();
		float maxTurnRadians = projectiles.TrackingTurnSpeedRadians[projectileIndex] * deltaSeconds;
		Vector2 steeredDirection = SteerDirection(currentDirection, desiredDirection, maxTurnRadians);
		projectiles.Velocity[projectileIndex] = steeredDirection * speed;
	}

	private static bool TryRefreshTrackedTarget(
		ProjectileStore projectiles,
		TargetStore targets,
		int projectileIndex)
	{
		if (!projectiles.TrackedTargetEntity[projectileIndex].IsValid)
		{
			projectiles.TrackedTargetIndex[projectileIndex] = -1;
			return false;
		}

		int cachedIndex = projectiles.TrackedTargetIndex[projectileIndex];
		if (cachedIndex >= 0
			&& cachedIndex < targets.Count
			&& targets.Entity[cachedIndex] == projectiles.TrackedTargetEntity[projectileIndex]
			&& IsValidTrackedTarget(projectiles, targets, projectileIndex, cachedIndex, false))
		{
			return true;
		}

		for (int i = 0; i < targets.Count; i++)
		{
			if (targets.Entity[i] != projectiles.TrackedTargetEntity[projectileIndex])
			{
				continue;
			}

			if (!IsValidTrackedTarget(projectiles, targets, projectileIndex, i, false))
			{
				break;
			}

			projectiles.TrackedTargetIndex[projectileIndex] = i;
			return true;
		}

		projectiles.TrackedTargetEntity[projectileIndex] = EntityHandle.Invalid;
		projectiles.TrackedTargetIndex[projectileIndex] = -1;
		return false;
	}

	private static bool TryAcquireTrackedTarget(
		ProjectileStore projectiles,
		TargetStore targets,
		int projectileIndex,
		Vector2 currentDirection)
	{
		projectiles.TrackedTargetEntity[projectileIndex] = EntityHandle.Invalid;
		projectiles.TrackedTargetIndex[projectileIndex] = -1;

		float bestDistanceSquared = float.MaxValue;

		for (int i = 0; i < targets.Count; i++)
		{
			if ((projectiles.TargetMask[projectileIndex] & targets.CollisionLayer[i]) == 0)
			{
				continue;
			}

			Vector2 toTarget = targets.Position[i] - projectiles.Position[projectileIndex];
			float distanceSquared = toTarget.LengthSquared();
			if (distanceSquared > projectiles.TrackingRangeSquared[projectileIndex])
			{
				continue;
			}

			if (distanceSquared <= MinimumDirectionLengthSquared)
			{
				projectiles.TrackedTargetEntity[projectileIndex] = targets.Entity[i];
				projectiles.TrackedTargetIndex[projectileIndex] = i;
				return true;
			}

			float inverseDistance = 1.0f / Mathf.Sqrt(distanceSquared);
			float forwardDot = currentDirection.Dot(toTarget * inverseDistance);
			if (forwardDot <= MinimumForwardDot || distanceSquared >= bestDistanceSquared)
			{
				continue;
			}

			bestDistanceSquared = distanceSquared;
			projectiles.TrackedTargetEntity[projectileIndex] = targets.Entity[i];
			projectiles.TrackedTargetIndex[projectileIndex] = i;
		}

		return projectiles.TrackedTargetEntity[projectileIndex].IsValid;
	}

	private static bool IsValidTrackedTarget(
		ProjectileStore projectiles,
		TargetStore targets,
		int projectileIndex,
		int targetIndex,
		bool requireForwardCone)
	{
		if ((projectiles.TargetMask[projectileIndex] & targets.CollisionLayer[targetIndex]) == 0)
		{
			return false;
		}

		Vector2 toTarget = targets.Position[targetIndex] - projectiles.Position[projectileIndex];
		float distanceSquared = toTarget.LengthSquared();
		if (distanceSquared > projectiles.TrackingRangeSquared[projectileIndex])
		{
			return false;
		}

		if (!requireForwardCone || distanceSquared <= MinimumDirectionLengthSquared)
		{
			return true;
		}

		Vector2 forward = projectiles.Velocity[projectileIndex].Normalized();
		float inverseDistance = 1.0f / Mathf.Sqrt(distanceSquared);
		return forward.Dot(toTarget * inverseDistance) > MinimumForwardDot;
	}

	private static Vector2 SteerDirection(Vector2 currentDirection, Vector2 desiredDirection, float maxTurnRadians)
	{
		if (maxTurnRadians <= 0.0f)
		{
			return currentDirection;
		}

		Vector2 directionDelta = desiredDirection - currentDirection;
		float deltaLengthSquared = directionDelta.LengthSquared();
		if (deltaLengthSquared <= MinimumDirectionLengthSquared)
		{
			return desiredDirection;
		}

		float deltaLength = Mathf.Sqrt(deltaLengthSquared);
		if (deltaLength > maxTurnRadians)
		{
			directionDelta *= maxTurnRadians / deltaLength;
		}

		Vector2 steered = currentDirection + directionDelta;
		return steered.LengthSquared() <= MinimumDirectionLengthSquared
			? currentDirection
			: steered.Normalized();
	}
}
