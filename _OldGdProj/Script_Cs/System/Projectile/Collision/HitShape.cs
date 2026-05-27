using Godot;
using System;

/// <summary>
/// Supported baked collision shape categories for projectile runtime math.
/// </summary>
public enum HitShapeKind
{
	Circle,
	Rectangle,
	Capsule
}

/// <summary>
/// Baked local collision shape data used by broad-phase bounds and narrow-phase checks.
/// </summary>
public readonly struct HitShape
{
	public readonly HitShapeKind Kind;
	public readonly Vector2 Offset;
	public readonly float Rotation;
	public readonly float Radius;
	public readonly Vector2 HalfExtents;
	public readonly Rect2 LocalBounds;

	public HitShape(
		HitShapeKind kind,
		Vector2 offset,
		float rotation,
		float radius,
		Vector2 halfExtents,
		Rect2 localBounds)
	{
		Kind = kind;
		Offset = offset;
		Rotation = rotation;
		Radius = radius;
		HalfExtents = halfExtents;
		LocalBounds = localBounds;
	}
}

/// <summary>
/// Projectile collision type definition registered with ProjectileWorld.
/// </summary>
public sealed class ProjectileDefinition
{
	public int TypeId;
	public HitShape Collision;

	public ProjectileDefinition(int typeId, HitShape collision)
	{
		TypeId = typeId;
		Collision = collision;
	}

	public ProjectileDefinition(int typeId, CollisionShape2D collisionShape)
		: this(typeId, HitShapeBaker.FromCollisionShape(collisionShape))
	{
	}

	public static ProjectileDefinition FromTemplate(
		int typeId,
		PackedScene template,
		NodePath collisionShapePath)
	{
		return new ProjectileDefinition(typeId, HitShapeBaker.FromTemplate(template, collisionShapePath));
	}
}

/// <summary>
/// Target collision type definition registered with ProjectileWorld.
/// </summary>
public sealed class TargetDefinition
{
	public int TypeId;
	public HitShape Collision;

	public TargetDefinition(int typeId, HitShape collision)
	{
		TypeId = typeId;
		Collision = collision;
	}

	public TargetDefinition(int typeId, CollisionShape2D collisionShape)
		: this(typeId, HitShapeBaker.FromCollisionShape(collisionShape))
	{
	}

	public static TargetDefinition FromTemplate(
		int typeId,
		PackedScene template,
		NodePath collisionShapePath)
	{
		return new TargetDefinition(typeId, HitShapeBaker.FromTemplate(template, collisionShapePath));
	}
}

/// <summary>
/// Converts Godot collision shapes or scene templates into baked HitShape values.
/// </summary>
public static class HitShapeBaker
{
	public static HitShape FromCollisionShape(CollisionShape2D node)
	{
		if (node.Shape == null)
		{
			throw new InvalidOperationException("CollisionShape2D needs a Shape2D before baking.");
		}

		return FromShape(node.Shape, node.Position, node.Rotation);
	}

	public static HitShape FromTemplate(PackedScene template, NodePath collisionShapePath)
	{
		Node instance = template.Instantiate<Node>();
		try
		{
			CollisionShape2D? collisionShape = instance.GetNodeOrNull<CollisionShape2D>(collisionShapePath);
			if (collisionShape == null)
			{
				throw new InvalidOperationException($"Projectile template needs a CollisionShape2D at '{collisionShapePath}'.");
			}

			return FromCollisionShape(collisionShape);
		}
		finally
		{
			instance.Free();
		}
	}

	public static HitShape FromShape(Shape2D shape)
	{
		return FromShape(shape, Vector2.Zero, 0.0f);
	}

	public static HitShape FromShape(Shape2D shape, Vector2 offset, float rotation)
	{
		if (shape is CircleShape2D circle)
		{
			float radius = circle.Radius;
			Vector2 radiusVector = new(radius, radius);
			return new HitShape(
				HitShapeKind.Circle,
				offset,
				rotation,
				radius,
				Vector2.Zero,
				new Rect2(offset - radiusVector, radiusVector * 2.0f));
		}

		if (shape is RectangleShape2D rectangle)
		{
			Vector2 halfExtents = rectangle.Size * 0.5f;
			return new HitShape(
				HitShapeKind.Rectangle,
				offset,
				rotation,
				0.0f,
				halfExtents,
				RectangleLocalBounds(offset, halfExtents, rotation));
		}

		if (shape is CapsuleShape2D capsule)
		{
			float radius = capsule.Radius;
			float halfSegment = Mathf.Max(0.0f, (capsule.Height * 0.5f) - radius);
			return new HitShape(
				HitShapeKind.Capsule,
				offset,
				rotation,
				radius,
				new Vector2(halfSegment, radius),
				CapsuleLocalBounds(offset, halfSegment, radius, rotation));
		}

		throw new InvalidOperationException($"Unsupported projectile collision shape: {shape.GetType().Name}");
	}

	private static Rect2 RectangleLocalBounds(Vector2 offset, Vector2 halfExtents, float rotation)
	{
		Vector2 xAxis = Rotate(new Vector2(1.0f, 0.0f), rotation);
		Vector2 yAxis = Rotate(new Vector2(0.0f, 1.0f), rotation);
		Vector2 half = new(
			Mathf.Abs(xAxis.X) * halfExtents.X + Mathf.Abs(yAxis.X) * halfExtents.Y,
			Mathf.Abs(xAxis.Y) * halfExtents.X + Mathf.Abs(yAxis.Y) * halfExtents.Y);

		return new Rect2(offset - half, half * 2.0f);
	}

	private static Rect2 CapsuleLocalBounds(Vector2 offset, float halfSegment, float radius, float rotation)
	{
		Vector2 axis = Rotate(new Vector2(0.0f, 1.0f), rotation);
		Vector2 a = offset - axis * halfSegment;
		Vector2 b = offset + axis * halfSegment;
		Vector2 min = new(
			Mathf.Min(a.X, b.X) - radius,
			Mathf.Min(a.Y, b.Y) - radius);
		Vector2 max = new(
			Mathf.Max(a.X, b.X) + radius,
			Mathf.Max(a.Y, b.Y) + radius);

		return new Rect2(min, max - min);
	}

	private static Vector2 Rotate(Vector2 vector, float radians)
	{
		float cosine = Mathf.Cos(radians);
		float sine = Mathf.Sin(radians);

		return new Vector2(
			(vector.X * cosine) - (vector.Y * sine),
			(vector.X * sine) + (vector.Y * cosine));
	}
}

/// <summary>
/// Allocation-free shape bounds and narrow-phase collision math for baked hit shapes.
/// </summary>
public static class HitShapeMath
{
	private const float Epsilon = 0.000001f;

	public static Rect2 ComputeWorldBounds(Vector2 ownerPosition, in HitShape shape)
	{
		return new Rect2(ownerPosition + shape.LocalBounds.Position, shape.LocalBounds.Size);
	}

	public static bool Hit(
		Vector2 aPosition,
		in HitShape a,
		Vector2 bPosition,
		in HitShape b)
	{
		return NarrowHit(aPosition, a, bPosition, b);
	}

	private static bool NarrowHit(
		Vector2 aPosition,
		in HitShape a,
		Vector2 bPosition,
		in HitShape b)
	{
		return (a.Kind, b.Kind) switch
		{
			(HitShapeKind.Circle, HitShapeKind.Circle) =>
				CircleCircle(aPosition, a, bPosition, b),
			(HitShapeKind.Circle, HitShapeKind.Rectangle) =>
				CircleRectangle(aPosition, a, bPosition, b),
			(HitShapeKind.Rectangle, HitShapeKind.Circle) =>
				CircleRectangle(bPosition, b, aPosition, a),
			(HitShapeKind.Circle, HitShapeKind.Capsule) =>
				CircleCapsule(aPosition, a, bPosition, b),
			(HitShapeKind.Capsule, HitShapeKind.Circle) =>
				CircleCapsule(bPosition, b, aPosition, a),
			(HitShapeKind.Rectangle, HitShapeKind.Rectangle) =>
				RectangleRectangle(aPosition, a, bPosition, b),
			(HitShapeKind.Rectangle, HitShapeKind.Capsule) =>
				RectangleCapsule(aPosition, a, bPosition, b),
			(HitShapeKind.Capsule, HitShapeKind.Rectangle) =>
				RectangleCapsule(bPosition, b, aPosition, a),
			(HitShapeKind.Capsule, HitShapeKind.Capsule) =>
				CapsuleCapsule(aPosition, a, bPosition, b),
			_ => false
		};
	}

	private static Rect2 CircleBounds(Vector2 center, float radius)
	{
		Vector2 radiusVector = new(radius, radius);
		return new Rect2(center - radiusVector, radiusVector * 2.0f);
	}

	private static Rect2 RectangleBounds(Vector2 center, Vector2 halfExtents, float rotation)
	{
		Vector2 xAxis = Rotate(new Vector2(1.0f, 0.0f), rotation);
		Vector2 yAxis = Rotate(new Vector2(0.0f, 1.0f), rotation);
		Vector2 half = new(
			Mathf.Abs(xAxis.X) * halfExtents.X + Mathf.Abs(yAxis.X) * halfExtents.Y,
			Mathf.Abs(xAxis.Y) * halfExtents.X + Mathf.Abs(yAxis.Y) * halfExtents.Y);

		return new Rect2(center - half, half * 2.0f);
	}

	private static Rect2 CapsuleBounds(Vector2 center, in HitShape capsule)
	{
		GetCapsuleSegmentFromCenter(center, capsule, out Vector2 a, out Vector2 b);
		Vector2 min = new(
			Mathf.Min(a.X, b.X) - capsule.Radius,
			Mathf.Min(a.Y, b.Y) - capsule.Radius);
		Vector2 max = new(
			Mathf.Max(a.X, b.X) + capsule.Radius,
			Mathf.Max(a.Y, b.Y) + capsule.Radius);

		return new Rect2(min, max - min);
	}

	private static bool CircleCircle(
		Vector2 aPosition,
		in HitShape a,
		Vector2 bPosition,
		in HitShape b)
	{
		Vector2 aCenter = aPosition + a.Offset;
		Vector2 bCenter = bPosition + b.Offset;
		float radius = a.Radius + b.Radius;

		return aCenter.DistanceSquaredTo(bCenter) <= radius * radius;
	}

	private static bool CircleRectangle(
		Vector2 circlePosition,
		in HitShape circle,
		Vector2 rectanglePosition,
		in HitShape rectangle)
	{
		Vector2 circleCenter = circlePosition + circle.Offset;
		Vector2 rectangleCenter = rectanglePosition + rectangle.Offset;
		Vector2 local = Rotate(circleCenter - rectangleCenter, -rectangle.Rotation);
		Vector2 closest = new(
			Mathf.Clamp(local.X, -rectangle.HalfExtents.X, rectangle.HalfExtents.X),
			Mathf.Clamp(local.Y, -rectangle.HalfExtents.Y, rectangle.HalfExtents.Y));

		return local.DistanceSquaredTo(closest) <= circle.Radius * circle.Radius;
	}

	private static bool CircleCapsule(
		Vector2 circlePosition,
		in HitShape circle,
		Vector2 capsulePosition,
		in HitShape capsule)
	{
		Vector2 circleCenter = circlePosition + circle.Offset;
		GetCapsuleSegment(capsulePosition, capsule, out Vector2 a, out Vector2 b);
		float radius = circle.Radius + capsule.Radius;

		return DistancePointSegmentSquared(circleCenter, a, b) <= radius * radius;
	}

	private static bool CapsuleCapsule(
		Vector2 aPosition,
		in HitShape a,
		Vector2 bPosition,
		in HitShape b)
	{
		GetCapsuleSegment(aPosition, a, out Vector2 a0, out Vector2 a1);
		GetCapsuleSegment(bPosition, b, out Vector2 b0, out Vector2 b1);
		float radius = a.Radius + b.Radius;

		return DistanceSegmentSegmentSquared(a0, a1, b0, b1) <= radius * radius;
	}

	private static bool RectangleRectangle(
		Vector2 aPosition,
		in HitShape a,
		Vector2 bPosition,
		in HitShape b)
	{
		GetRectangleCorners(aPosition + a.Offset, a, out Vector2 a0, out Vector2 a1, out Vector2 a2, out Vector2 a3);
		GetRectangleCorners(bPosition + b.Offset, b, out Vector2 b0, out Vector2 b1, out Vector2 b2, out Vector2 b3);

		return OverlapsOnAxes(a0, a1, a2, a3, b0, b1, b2, b3)
			&& OverlapsOnAxes(b0, b1, b2, b3, a0, a1, a2, a3);
	}

	private static bool RectangleCapsule(
		Vector2 rectanglePosition,
		in HitShape rectangle,
		Vector2 capsulePosition,
		in HitShape capsule)
	{
		Vector2 rectangleCenter = rectanglePosition + rectangle.Offset;
		GetCapsuleSegment(capsulePosition, capsule, out Vector2 a, out Vector2 b);

		a = Rotate(a - rectangleCenter, -rectangle.Rotation);
		b = Rotate(b - rectangleCenter, -rectangle.Rotation);
		float distanceSquared = DistanceSegmentAabbSquared(
			a,
			b,
			-rectangle.HalfExtents,
			rectangle.HalfExtents);

		return distanceSquared <= capsule.Radius * capsule.Radius;
	}

	private static void GetCapsuleSegment(
		Vector2 ownerPosition,
		in HitShape capsule,
		out Vector2 a,
		out Vector2 b)
	{
		GetCapsuleSegmentFromCenter(ownerPosition + capsule.Offset, capsule, out a, out b);
	}

	private static void GetCapsuleSegmentFromCenter(
		Vector2 center,
		in HitShape capsule,
		out Vector2 a,
		out Vector2 b)
	{
		Vector2 axis = Rotate(new Vector2(0.0f, 1.0f), capsule.Rotation);
		float halfSegment = capsule.HalfExtents.X;
		a = center - axis * halfSegment;
		b = center + axis * halfSegment;
	}

	private static float DistanceSegmentAabbSquared(
		Vector2 a,
		Vector2 b,
		Vector2 min,
		Vector2 max)
	{
		if (SegmentIntersectsAabb(a, b, min, max))
		{
			return 0.0f;
		}

		float distanceSquared = float.PositiveInfinity;
		distanceSquared = Mathf.Min(distanceSquared, DistancePointAabbSquared(a, min, max));
		distanceSquared = Mathf.Min(distanceSquared, DistancePointAabbSquared(b, min, max));

		Vector2 c0 = new(min.X, min.Y);
		Vector2 c1 = new(max.X, min.Y);
		Vector2 c2 = new(max.X, max.Y);
		Vector2 c3 = new(min.X, max.Y);
		distanceSquared = Mathf.Min(distanceSquared, DistancePointSegmentSquared(c0, a, b));
		distanceSquared = Mathf.Min(distanceSquared, DistancePointSegmentSquared(c1, a, b));
		distanceSquared = Mathf.Min(distanceSquared, DistancePointSegmentSquared(c2, a, b));
		distanceSquared = Mathf.Min(distanceSquared, DistancePointSegmentSquared(c3, a, b));

		return distanceSquared;
	}

	private static float DistancePointAabbSquared(Vector2 point, Vector2 min, Vector2 max)
	{
		float closestX = Mathf.Clamp(point.X, min.X, max.X);
		float closestY = Mathf.Clamp(point.Y, min.Y, max.Y);
		return point.DistanceSquaredTo(new Vector2(closestX, closestY));
	}

	private static float DistanceSegmentSegmentSquared(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
	{
		if (SegmentsIntersect(a0, a1, b0, b1))
		{
			return 0.0f;
		}

		float d1 = DistancePointSegmentSquared(a0, b0, b1);
		float d2 = DistancePointSegmentSquared(a1, b0, b1);
		float d3 = DistancePointSegmentSquared(b0, a0, a1);
		float d4 = DistancePointSegmentSquared(b1, a0, a1);

		return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
	}

	private static float DistancePointSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float lengthSquared = ab.LengthSquared();

		if (lengthSquared <= Epsilon)
		{
			return point.DistanceSquaredTo(a);
		}

		float t = (point - a).Dot(ab) / lengthSquared;
		t = Mathf.Clamp(t, 0.0f, 1.0f);
		Vector2 closest = a + ab * t;

		return point.DistanceSquaredTo(closest);
	}

	private static bool SegmentIntersectsAabb(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
	{
		if (PointInAabb(a, min, max) || PointInAabb(b, min, max))
		{
			return true;
		}

		Vector2 c0 = new(min.X, min.Y);
		Vector2 c1 = new(max.X, min.Y);
		Vector2 c2 = new(max.X, max.Y);
		Vector2 c3 = new(min.X, max.Y);

		return SegmentsIntersect(a, b, c0, c1)
			|| SegmentsIntersect(a, b, c1, c2)
			|| SegmentsIntersect(a, b, c2, c3)
			|| SegmentsIntersect(a, b, c3, c0);
	}

	private static bool PointInAabb(Vector2 point, Vector2 min, Vector2 max)
	{
		return point.X >= min.X
			&& point.X <= max.X
			&& point.Y >= min.Y
			&& point.Y <= max.Y;
	}

	private static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
	{
		float d1 = Cross(a1 - a0, b0 - a0);
		float d2 = Cross(a1 - a0, b1 - a0);
		float d3 = Cross(b1 - b0, a0 - b0);
		float d4 = Cross(b1 - b0, a1 - b0);

		if (((d1 > 0.0f && d2 < 0.0f) || (d1 < 0.0f && d2 > 0.0f))
			&& ((d3 > 0.0f && d4 < 0.0f) || (d3 < 0.0f && d4 > 0.0f)))
		{
			return true;
		}

		return Mathf.Abs(d1) <= Epsilon && PointOnSegment(b0, a0, a1)
			|| Mathf.Abs(d2) <= Epsilon && PointOnSegment(b1, a0, a1)
			|| Mathf.Abs(d3) <= Epsilon && PointOnSegment(a0, b0, b1)
			|| Mathf.Abs(d4) <= Epsilon && PointOnSegment(a1, b0, b1);
	}

	private static bool PointOnSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		return point.X >= Mathf.Min(a.X, b.X) - Epsilon
			&& point.X <= Mathf.Max(a.X, b.X) + Epsilon
			&& point.Y >= Mathf.Min(a.Y, b.Y) - Epsilon
			&& point.Y <= Mathf.Max(a.Y, b.Y) + Epsilon;
	}

	private static void GetRectangleCorners(
		Vector2 center,
		in HitShape rectangle,
		out Vector2 c0,
		out Vector2 c1,
		out Vector2 c2,
		out Vector2 c3)
	{
		Vector2 xAxis = Rotate(new Vector2(1.0f, 0.0f), rectangle.Rotation);
		Vector2 yAxis = Rotate(new Vector2(0.0f, 1.0f), rectangle.Rotation);
		Vector2 xHalf = xAxis * rectangle.HalfExtents.X;
		Vector2 yHalf = yAxis * rectangle.HalfExtents.Y;

		c0 = center - xHalf - yHalf;
		c1 = center + xHalf - yHalf;
		c2 = center + xHalf + yHalf;
		c3 = center - xHalf + yHalf;
	}

	private static bool OverlapsOnAxes(
		Vector2 a0,
		Vector2 a1,
		Vector2 a2,
		Vector2 a3,
		Vector2 b0,
		Vector2 b1,
		Vector2 b2,
		Vector2 b3)
	{
		Vector2 axis0 = (a1 - a0).Normalized();
		Vector2 axis1 = (a3 - a0).Normalized();

		return OverlapsOnAxis(axis0, a0, a1, a2, a3, b0, b1, b2, b3)
			&& OverlapsOnAxis(axis1, a0, a1, a2, a3, b0, b1, b2, b3);
	}

	private static bool OverlapsOnAxis(
		Vector2 axis,
		Vector2 a0,
		Vector2 a1,
		Vector2 a2,
		Vector2 a3,
		Vector2 b0,
		Vector2 b1,
		Vector2 b2,
		Vector2 b3)
	{
		ProjectOntoAxis(axis, a0, a1, a2, a3, out float aMin, out float aMax);
		ProjectOntoAxis(axis, b0, b1, b2, b3, out float bMin, out float bMax);

		return aMax >= bMin && bMax >= aMin;
	}

	private static void ProjectOntoAxis(
		Vector2 axis,
		Vector2 c0,
		Vector2 c1,
		Vector2 c2,
		Vector2 c3,
		out float min,
		out float max)
	{
		float p0 = c0.Dot(axis);
		float p1 = c1.Dot(axis);
		float p2 = c2.Dot(axis);
		float p3 = c3.Dot(axis);

		min = Mathf.Min(Mathf.Min(p0, p1), Mathf.Min(p2, p3));
		max = Mathf.Max(Mathf.Max(p0, p1), Mathf.Max(p2, p3));
	}

	private static Vector2 Rotate(Vector2 vector, float radians)
	{
		float cosine = Mathf.Cos(radians);
		float sine = Mathf.Sin(radians);

		return new Vector2(
			(vector.X * cosine) - (vector.Y * sine),
			(vector.X * sine) + (vector.Y * cosine));
	}

	private static float Cross(Vector2 left, Vector2 right)
	{
		return (left.X * right.Y) - (left.Y * right.X);
	}
}
