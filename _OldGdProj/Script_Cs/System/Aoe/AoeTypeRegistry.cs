using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Adapter-side cache for AOE effect and target type ids baked from Godot scene data.
/// </summary>
internal sealed class AoeTypeRegistry
{
	private readonly AoeWorld _world;
	private readonly AoeRenderer _renderer;
	private readonly Dictionary<AoeTypeKey, int> _aoeTypeCache = new();
	private readonly Dictionary<TargetTypeKey, int> _targetTypeCache = new();
	private int _nextAoeTypeId;
	private int _nextTargetTypeId;

	public AoeTypeRegistry(AoeWorld world, AoeRenderer renderer)
	{
		_world = world;
		_renderer = renderer;
	}

	public int RegisterAoeType(
		PackedScene template,
		NodePath collisionShapePath,
		int maximumAoeListSize)
	{
		AoeTypeKey cacheKey = new(template, collisionShapePath);
		if (_aoeTypeCache.TryGetValue(cacheKey, out int cachedTypeId))
		{
			return cachedTypeId;
		}

		int typeId = _nextAoeTypeId++;
		_world.RegisterAoeType(typeId, HitShapeBaker.FromTemplate(template, collisionShapePath));
		_renderer.RegisterAoeType(typeId, AoeRenderDefinitionBaker.FromTemplate(template), maximumAoeListSize);
		_aoeTypeCache.Add(cacheKey, typeId);
		return typeId;
	}

	public int RegisterTargetType(CollisionShape2D hurtboxShape)
	{
		TargetTypeKey cacheKey = new(hurtboxShape);
		if (_targetTypeCache.TryGetValue(cacheKey, out int cachedTypeId))
		{
			return cachedTypeId;
		}

		int typeId = _nextTargetTypeId++;
		_world.RegisterTargetType(typeId, HitShapeBaker.FromCollisionShape(hurtboxShape));
		_targetTypeCache.Add(cacheKey, typeId);
		return typeId;
	}

	public void ValidateRegisteredAoeType(int aoeTypeId)
	{
		if (aoeTypeId < 0 || aoeTypeId >= _nextAoeTypeId)
		{
			throw new InvalidOperationException(
				$"AOE type id {aoeTypeId} is not registered. Register AOE templates before spawning them.");
		}
	}

	private readonly struct AoeTypeKey : IEquatable<AoeTypeKey>
	{
		private readonly string _templatePath;
		private readonly ulong _templateInstanceId;
		private readonly string _collisionShapePath;

		public AoeTypeKey(PackedScene template, NodePath collisionShapePath)
		{
			_templatePath = template.ResourcePath;
			_templateInstanceId = string.IsNullOrEmpty(_templatePath) ? template.GetInstanceId() : 0UL;
			_collisionShapePath = collisionShapePath.ToString();
		}

		public bool Equals(AoeTypeKey other)
		{
			return _templatePath == other._templatePath
				&& _templateInstanceId == other._templateInstanceId
				&& _collisionShapePath == other._collisionShapePath;
		}

		public override bool Equals(object? obj)
		{
			return obj is AoeTypeKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(_templatePath, _templateInstanceId, _collisionShapePath);
		}
	}

	private readonly struct TargetTypeKey : IEquatable<TargetTypeKey>
	{
		private readonly Rid _shapeRid;
		private readonly Vector2 _position;
		private readonly float _rotation;

		public TargetTypeKey(CollisionShape2D shape)
		{
			_shapeRid = shape.Shape?.GetRid() ?? default;
			_position = shape.Position;
			_rotation = shape.Rotation;
		}

		public bool Equals(TargetTypeKey other)
		{
			return _shapeRid == other._shapeRid
				&& _position == other._position
				&& Mathf.IsEqualApprox(_rotation, other._rotation);
		}

		public override bool Equals(object? obj)
		{
			return obj is TargetTypeKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(_shapeRid, _position, _rotation);
		}
	}
}
