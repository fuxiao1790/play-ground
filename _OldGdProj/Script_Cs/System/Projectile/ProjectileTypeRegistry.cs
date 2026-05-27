using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Adapter-side cache for projectile and target type ids baked from Godot scene data.
/// </summary>
internal sealed class ProjectileTypeRegistry
{
	private readonly ProjectileWorld _world;
	private readonly Renderer _renderer;
	private readonly Dictionary<ProjectileTypeKey, int> _projectileTypeCache = new();
	private readonly Dictionary<TargetTypeKey, int> _targetTypeCache = new();
	private int _nextProjectileTypeId;
	private int _nextTargetTypeId;

	public ProjectileTypeRegistry(ProjectileWorld world, Renderer renderer)
	{
		_world = world;
		_renderer = renderer;
	}

	public int RegisterProjectileType(
		PackedScene template,
		NodePath collisionShapePath,
		int maximumProjectileListSize)
	{
		ProjectileTypeKey cacheKey = new(template, collisionShapePath);
		if (_projectileTypeCache.TryGetValue(cacheKey, out int cachedTypeId))
		{
			return cachedTypeId;
		}

		int typeId = _nextProjectileTypeId++;
		_world.RegisterProjectileType(ProjectileDefinition.FromTemplate(typeId, template, collisionShapePath));
		_renderer.RegisterProjectileType(typeId, RenderDefinitionBaker.FromTemplate(template), maximumProjectileListSize);
		_projectileTypeCache.Add(cacheKey, typeId);
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
		_world.RegisterTargetType(new TargetDefinition(typeId, hurtboxShape));
		_targetTypeCache.Add(cacheKey, typeId);
		return typeId;
	}

	public void ValidateRegisteredProjectileType(int projectileTypeId)
	{
		if (projectileTypeId < 0 || projectileTypeId >= _nextProjectileTypeId)
		{
			throw new InvalidOperationException(
				$"Projectile type id {projectileTypeId} is not registered. Register projectile templates before spawning them.");
		}
	}

	private readonly struct ProjectileTypeKey : IEquatable<ProjectileTypeKey>
	{
		private readonly string _templatePath;
		private readonly ulong _templateInstanceId;
		private readonly string _collisionShapePath;

		public ProjectileTypeKey(PackedScene template, NodePath collisionShapePath)
		{
			_templatePath = template.ResourcePath;
			_templateInstanceId = string.IsNullOrEmpty(_templatePath) ? template.GetInstanceId() : 0UL;
			_collisionShapePath = collisionShapePath.ToString();
		}

		public bool Equals(ProjectileTypeKey other)
		{
			return _templatePath == other._templatePath
				&& _templateInstanceId == other._templateInstanceId
				&& _collisionShapePath == other._collisionShapePath;
		}

		public override bool Equals(object? obj)
		{
			return obj is ProjectileTypeKey other && Equals(other);
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
