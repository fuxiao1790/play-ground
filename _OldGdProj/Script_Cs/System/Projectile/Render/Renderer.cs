using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Godot-facing projectile renderer that stores one MultiMesh batch per projectile type.
/// </summary>
public partial class Renderer : MultiMeshInstance2D
{
	private static readonly Vector2 HiddenOrigin = new(-1000000.0f, -1000000.0f);
	private static readonly Color HiddenColor = new(1.0f, 1.0f, 1.0f, 0.0f);
	private static readonly Mesh UnitQuadMesh = CreateUnitQuadMesh();
	private const int ProjectileZIndex = 50;
	private const float VisibilityPadding = 32.0f;
	private const int TransformFloatCount2D = 8;
	private const int ColorFloatCount = 4;
	private const int FloatsPerInstance = TransformFloatCount2D + ColorFloatCount;
	private static readonly Rect2 FixedVisibilityRect = new(
		new Vector2(-1000000.0f, -1000000.0f),
		new Vector2(2000000.0f, 2000000.0f));
	private readonly Dictionary<int, BatchRecord> _batches = new();
	private readonly List<RenderRangeCommands> _renderRangeCommands = new(1);
	private readonly IProjectileWorkScheduler _parallelScheduler = new TaskProjectileWorkScheduler();
	private readonly object _renderRangeCommandsLock = new();
	private RenderPrepDefinition[] _prepDefinitionsByTypeId = Array.Empty<RenderPrepDefinition>();
	private bool[] _assignedPrepDefinitions = Array.Empty<bool>();
	private int _nextBatchNodeId;
	private long _lastRenderPreparationMicroseconds;
	private long _lastThreadedRenderPreparationMicroseconds;
	private int _lastThreadedRenderWorkerCount;
	private int _lastThreadedRenderChunkCount;
	private bool _lastRenderPreparationUsedThreading;

	public override void _Ready()
	{
		ConfigureBatchNode(this);
	}

	public void RegisterProjectileType(int typeId, in RenderDefinition definition, int maxInstances)
	{
		if (_batches.ContainsKey(typeId))
		{
			return;
		}

		if (maxInstances <= 0)
		{
			throw new InvalidOperationException("Renderer requires a positive projectile capacity.");
		}

		MultiMeshInstance2D node = _batches.Count == 0 ? this : CreateBatchNode(typeId);
		MultiMesh multiMesh = CreateMultiMesh();
		node.Texture = definition.Texture;
		node.Multimesh = multiMesh;
		SetPrepDefinition(typeId, new RenderPrepDefinition(definition));

		BatchRecord batch = new(typeId, node, multiMesh, definition, maxInstances);
		for (int slot = maxInstances - 1; slot >= 0; slot--)
		{
			batch.FreeSlots.Push(slot);
			SetHidden(batch, slot);
		}

		EnsureVisibilityRect(batch);
		UploadBatch(batch);
		_batches.Add(typeId, batch);
	}

	public void BeginFrame()
	{
	}

	public void OnSpawn(ProjectileStore projectiles, int projectileIndex)
	{
		BatchRecord batch = GetBatch(projectiles.TypeId[projectileIndex]);
		if (batch.FreeSlots.Count == 0)
		{
			throw new InvalidOperationException($"Projectile render batch {projectiles.TypeId[projectileIndex]} is full.");
		}

		int slot = batch.FreeSlots.Pop();
		projectiles.RenderSlotIndex[projectileIndex] = slot;
		projectiles.RenderRotation[projectileIndex] = FacingRotation(projectiles.Velocity[projectileIndex]);
		projectiles.RenderVelocity[projectileIndex] = projectiles.Velocity[projectileIndex];
		batch.LiveCount++;
		batch.OccupiedSlots[slot] = true;
		if (slot + 1 > batch.VisibleSlotCount)
		{
			batch.VisibleSlotCount = slot + 1;
		}

		SetColor(batch, slot, batch.Definition.Modulate);
		EnsureVisibilityRect(batch);
		Apply(projectiles, projectileIndex, batch);
	}

	public void Apply(ProjectileStore projectiles, int projectileIndex)
	{
		if (projectiles.RenderSlotIndex[projectileIndex] < 0)
		{
			return;
		}

		BatchRecord batch = GetBatch(projectiles.TypeId[projectileIndex]);
		Apply(projectiles, projectileIndex, batch);
	}

	public void Apply(ProjectileStore projectiles, int startIndex, int endIndex)
	{
		Apply(projectiles, startIndex, endIndex, false, int.MaxValue, 1);
	}

	public void Apply(
		ProjectileStore projectiles,
		int startIndex,
		int endIndex,
		bool parallelPreparationEnabled,
		int minimumParallelCount,
		int chunkSize)
	{
		BeginFrame();

		ResetRenderPreparationCounters();
		int count = endIndex - startIndex;
		if (parallelPreparationEnabled && count >= Math.Max(1, minimumParallelCount))
		{
			ApplyPreparedRenderCommands(projectiles, startIndex, count, Math.Max(1, chunkSize));
			EndFrame();
			return;
		}

		long preparationStartTicks = Stopwatch.GetTimestamp();
		int cachedTypeId = int.MinValue;
		BatchRecord? cachedBatch = null;
		for (int i = startIndex; i < endIndex; i++)
		{
			if (projectiles.RenderSlotIndex[i] < 0)
			{
				continue;
			}

			if (projectiles.TypeId[i] != cachedTypeId || cachedBatch == null)
			{
				cachedTypeId = projectiles.TypeId[i];
				cachedBatch = GetBatch(projectiles.TypeId[i]);
			}

			Apply(projectiles, i, cachedBatch);
		}

		_lastRenderPreparationMicroseconds = ElapsedMicroseconds(preparationStartTicks);
		EndFrame();
	}

	public void OnDespawn(in ProjectileSnapshot data)
	{
		if (data.RenderSlotIndex < 0 || !_batches.TryGetValue(data.TypeId, out BatchRecord? batch))
		{
			return;
		}

		int slot = data.RenderSlotIndex;
		if (!batch.OccupiedSlots[slot])
		{
			return;
		}

		batch.OccupiedSlots[slot] = false;
		batch.FreeSlots.Push(slot);
		batch.LiveCount = Math.Max(0, batch.LiveCount - 1);
		SetHidden(batch, slot);

		if (slot + 1 == batch.VisibleSlotCount)
		{
			int visibleSlots = slot;
			while (visibleSlots > 0 && !batch.OccupiedSlots[visibleSlots - 1])
			{
				visibleSlots--;
			}

			batch.VisibleSlotCount = visibleSlots;
		}
		else if (batch.LiveCount == 0)
		{
			batch.VisibleSlotCount = 0;
		}

		if (batch.LiveCount == 0)
		{
			ClearVisibilityRect(batch);
		}
	}

	public void EndFrame()
	{
		foreach (BatchRecord batch in _batches.Values)
		{
			if (batch.IsDirty)
			{
				UploadBatch(batch);
			}
		}
	}

	public void Reset()
	{
		foreach (BatchRecord batch in _batches.Values)
		{
			for (int slot = 0; slot < batch.Capacity; slot++)
			{
				SetHidden(batch, slot);
				batch.OccupiedSlots[slot] = false;
			}

			batch.FreeSlots.Clear();
			for (int slot = batch.Capacity - 1; slot >= 0; slot--)
			{
				batch.FreeSlots.Push(slot);
			}

			batch.LiveCount = 0;
			batch.VisibleSlotCount = 0;
			ClearVisibilityRect(batch);
			UploadBatch(batch);
		}
	}

	public int GetBatchCount()
	{
		return _batches.Count;
	}

	public int GetLiveInstanceCount()
	{
		int total = 0;
		foreach (BatchRecord batch in _batches.Values)
		{
			total += batch.LiveCount;
		}

		return total;
	}

	public int GetLiveInstanceCountForType(int typeId)
	{
		return _batches.TryGetValue(typeId, out BatchRecord? batch) ? batch.LiveCount : 0;
	}

	public Transform2D GetInstanceTransform2DForType(int typeId, int slot)
	{
		BatchRecord batch = GetBatch(typeId);
		if (slot < 0 || slot >= batch.Capacity)
		{
			throw new ArgumentOutOfRangeException(nameof(slot));
		}

		return batch.CachedTransforms[slot];
	}

	public Vector2 GetInstanceOriginForType(int typeId, int slot)
	{
		return GetInstanceTransform2DForType(typeId, slot).Origin;
	}

	public float GetInstanceRotationForType(int typeId, int slot)
	{
		return GetInstanceTransform2DForType(typeId, slot).X.Angle();
	}

	public int GetFirstLiveSlotForType(int typeId)
	{
		BatchRecord batch = GetBatch(typeId);
		for (int slot = 0; slot < batch.Capacity; slot++)
		{
			if (batch.OccupiedSlots[slot])
			{
				return slot;
			}
		}

		return -1;
	}

	public long GetLastRenderPreparationMicroseconds()
	{
		return _lastRenderPreparationMicroseconds;
	}

	public long GetLastThreadedRenderPreparationMicroseconds()
	{
		return _lastThreadedRenderPreparationMicroseconds;
	}

	public int GetLastThreadedRenderWorkerCount()
	{
		return _lastThreadedRenderWorkerCount;
	}

	public int GetLastThreadedRenderChunkCount()
	{
		return _lastThreadedRenderChunkCount;
	}

	public bool WasLastRenderPreparationThreaded()
	{
		return _lastRenderPreparationUsedThreading;
	}

	private BatchRecord GetBatch(int typeId)
	{
		if (!_batches.TryGetValue(typeId, out BatchRecord? batch))
		{
			throw new InvalidOperationException($"Renderer has no projectile batch registered for type {typeId}.");
		}

		return batch;
	}

	private void ApplyPreparedRenderCommands(
		ProjectileStore projectiles,
		int startIndex,
		int count,
		int chunkSize)
	{
		long preparationStartTicks = Stopwatch.GetTimestamp();
		int threadedChunkCount = TaskProjectileWorkScheduler.ChunkCount(count, chunkSize);
		_renderRangeCommands.Clear();
		_parallelScheduler.ForEachRange(count, chunkSize, (rangeStart, rangeEnd) =>
		{
			RenderRangeCommands commands = new(startIndex + rangeStart);
			PrepareRenderRange(projectiles, startIndex + rangeStart, startIndex + rangeEnd, commands);
			AddRenderRangeCommands(commands);
		});

		_renderRangeCommands.Sort(static (left, right) => left.StartIndex.CompareTo(right.StartIndex));
		for (int i = 0; i < _renderRangeCommands.Count; i++)
		{
			RenderRangeCommands commands = _renderRangeCommands[i];
			for (int commandIndex = 0; commandIndex < commands.Commands.Count; commandIndex++)
			{
				ApplyPreparedCommand(commands.Commands[commandIndex]);
			}
		}

		_renderRangeCommands.Clear();
		_lastRenderPreparationMicroseconds = ElapsedMicroseconds(preparationStartTicks);
		_lastThreadedRenderPreparationMicroseconds = _lastRenderPreparationMicroseconds;
		_lastThreadedRenderChunkCount = threadedChunkCount;
		_lastThreadedRenderWorkerCount = TaskProjectileWorkScheduler.WorkerCountFor(threadedChunkCount);
		_lastRenderPreparationUsedThreading = true;
	}

	private void PrepareRenderRange(
		ProjectileStore projectiles,
		int startIndex,
		int endIndex,
		RenderRangeCommands commands)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			int slot = projectiles.RenderSlotIndex[i];
			if (slot < 0)
			{
				continue;
			}

			int typeId = projectiles.TypeId[i];
			RenderPrepDefinition definition = GetPrepDefinition(typeId);
			if (projectiles.Velocity[i] != projectiles.RenderVelocity[i])
			{
				projectiles.RenderVelocity[i] = projectiles.Velocity[i];
				projectiles.RenderRotation[i] = FacingRotation(projectiles.Velocity[i]);
			}

			BuildTransform(
				projectiles.Position[i],
				projectiles.RenderRotation[i],
				in definition,
				out Vector2 xAxis,
				out Vector2 yAxis,
				out Vector2 origin);
			commands.Commands.Add(new PreparedRenderCommand(typeId, slot, xAxis, yAxis, origin));
		}
	}

	private void AddRenderRangeCommands(RenderRangeCommands commands)
	{
		lock (_renderRangeCommandsLock)
		{
			_renderRangeCommands.Add(commands);
		}
	}

	private void ApplyPreparedCommand(in PreparedRenderCommand command)
	{
		BatchRecord batch = GetBatch(command.TypeId);
		SetTransform(batch, command.Slot, command.XAxis, command.YAxis, command.Origin);
	}

	private RenderPrepDefinition GetPrepDefinition(int typeId)
	{
		if (typeId < 0 || typeId >= _assignedPrepDefinitions.Length || !_assignedPrepDefinitions[typeId])
		{
			throw new InvalidOperationException($"Renderer has no prep definition registered for type {typeId}.");
		}

		return _prepDefinitionsByTypeId[typeId];
	}

	private void SetPrepDefinition(int typeId, in RenderPrepDefinition definition)
	{
		if (typeId < 0)
		{
			throw new InvalidOperationException("Renderer type id must be zero or greater.");
		}

		while (_prepDefinitionsByTypeId.Length <= typeId)
		{
			int newCapacity = Math.Max(1, _prepDefinitionsByTypeId.Length * 2);
			Array.Resize(ref _prepDefinitionsByTypeId, newCapacity);
			Array.Resize(ref _assignedPrepDefinitions, newCapacity);
		}

		_prepDefinitionsByTypeId[typeId] = definition;
		_assignedPrepDefinitions[typeId] = true;
	}

	private void ResetRenderPreparationCounters()
	{
		_lastRenderPreparationMicroseconds = 0;
		_lastThreadedRenderPreparationMicroseconds = 0;
		_lastThreadedRenderWorkerCount = 0;
		_lastThreadedRenderChunkCount = 0;
		_lastRenderPreparationUsedThreading = false;
	}

	private static long ElapsedMicroseconds(long startTicks)
	{
		return ((Stopwatch.GetTimestamp() - startTicks) * 1000000L) / Stopwatch.Frequency;
	}

	private MultiMeshInstance2D CreateBatchNode(int typeId)
	{
		Node? parent = GetParent();
		if (parent == null)
		{
			throw new InvalidOperationException("Renderer needs a parent node before registering projectile types.");
		}

		var node = new MultiMeshInstance2D
		{
			Name = $"MultiMeshInstance2D_Batch_{typeId}_{_nextBatchNodeId++}",
			Visible = Visible,
			Position = Position,
			Rotation = Rotation,
			Scale = Scale,
			TextureFilter = TextureFilter,
			TextureRepeat = TextureRepeat
		};
		ConfigureBatchNode(node);
		parent.AddChild(node);
		return node;
	}

	private static void ConfigureBatchNode(MultiMeshInstance2D node)
	{
		node.ZAsRelative = false;
		node.ZIndex = ProjectileZIndex;
	}

	private static MultiMesh CreateMultiMesh()
	{
		MultiMesh multiMesh = new();
		multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		multiMesh.UseColors = true;
		multiMesh.Mesh = UnitQuadMesh;
		multiMesh.InstanceCount = 0;
		multiMesh.VisibleInstanceCount = 0;
		return multiMesh;
	}

	private static Mesh CreateUnitQuadMesh()
	{
		SurfaceTool surfaceTool = new();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		surfaceTool.SetUV(new Vector2(0.0f, 0.0f));
		surfaceTool.AddVertex(new Vector3(-0.5f, -0.5f, 0.0f));
		surfaceTool.SetUV(new Vector2(1.0f, 0.0f));
		surfaceTool.AddVertex(new Vector3(0.5f, -0.5f, 0.0f));
		surfaceTool.SetUV(new Vector2(1.0f, 1.0f));
		surfaceTool.AddVertex(new Vector3(0.5f, 0.5f, 0.0f));

		surfaceTool.SetUV(new Vector2(0.0f, 0.0f));
		surfaceTool.AddVertex(new Vector3(-0.5f, -0.5f, 0.0f));
		surfaceTool.SetUV(new Vector2(1.0f, 1.0f));
		surfaceTool.AddVertex(new Vector3(0.5f, 0.5f, 0.0f));
		surfaceTool.SetUV(new Vector2(0.0f, 1.0f));
		surfaceTool.AddVertex(new Vector3(-0.5f, 0.5f, 0.0f));

		return surfaceTool.Commit();
	}

	private static void Apply(ProjectileStore projectiles, int projectileIndex, BatchRecord batch)
	{
		if (projectiles.Velocity[projectileIndex] != projectiles.RenderVelocity[projectileIndex])
		{
			projectiles.RenderVelocity[projectileIndex] = projectiles.Velocity[projectileIndex];
			projectiles.RenderRotation[projectileIndex] = FacingRotation(projectiles.Velocity[projectileIndex]);
		}

		SetTransform(
			batch,
			projectiles.RenderSlotIndex[projectileIndex],
			projectiles.Position[projectileIndex],
			projectiles.RenderRotation[projectileIndex]);
	}

	private static void SetTransform(
		BatchRecord batch,
		int slot,
		Vector2 position,
		float runtimeRotation)
	{
		BuildTransform(
			position,
			runtimeRotation,
			batch.PrepDefinition,
			out Vector2 xAxis,
			out Vector2 yAxis,
			out Vector2 origin);
		SetTransform(batch, slot, xAxis, yAxis, origin);
	}

	private static void BuildTransform(
		Vector2 position,
		float runtimeRotation,
		in RenderPrepDefinition definition,
		out Vector2 xAxis,
		out Vector2 yAxis,
		out Vector2 origin)
	{
		float runtimeCosine = Mathf.Cos(runtimeRotation);
		float runtimeSine = Mathf.Sin(runtimeRotation);
		float totalCosine = (runtimeCosine * definition.LocalRotationCosine)
			- (runtimeSine * definition.LocalRotationSine);
		float totalSine = (runtimeSine * definition.LocalRotationCosine)
			+ (runtimeCosine * definition.LocalRotationSine);
		origin = position + new Vector2(
			(definition.LocalOrigin.X * runtimeCosine) - (definition.LocalOrigin.Y * runtimeSine),
			(definition.LocalOrigin.X * runtimeSine) + (definition.LocalOrigin.Y * runtimeCosine));
		xAxis = new(
			definition.LocalScale.X * totalCosine,
			definition.LocalScale.X * totalSine);
		yAxis = new(
			-definition.LocalScale.Y * totalSine,
			definition.LocalScale.Y * totalCosine);
	}

	private static float FacingRotation(Vector2 velocity)
	{
		return velocity.LengthSquared() > 0.000001f
			? Mathf.Atan2(velocity.Y, velocity.X)
			: 0.0f;
	}

	private static void SetHidden(BatchRecord batch, int slot)
	{
		SetTransform(batch, slot, Vector2.Zero, Vector2.Zero, HiddenOrigin);
		SetColor(batch, slot, HiddenColor);
	}

	private static void SetTransform(BatchRecord batch, int slot, Vector2 xAxis, Vector2 yAxis, Vector2 origin)
	{
		Transform2D transform = new(xAxis, yAxis, origin);
		batch.CachedTransforms[slot] = transform;
		int offset = slot * FloatsPerInstance;
		batch.Buffer[offset + 0] = xAxis.X;
		batch.Buffer[offset + 1] = yAxis.X;
		batch.Buffer[offset + 2] = 0.0f;
		batch.Buffer[offset + 3] = origin.X;
		batch.Buffer[offset + 4] = xAxis.Y;
		batch.Buffer[offset + 5] = yAxis.Y;
		batch.Buffer[offset + 6] = 0.0f;
		batch.Buffer[offset + 7] = origin.Y;
		batch.IsDirty = true;
	}

	private static void SetColor(BatchRecord batch, int slot, Color color)
	{
		int offset = slot * FloatsPerInstance + TransformFloatCount2D;
		batch.Buffer[offset + 0] = color.R;
		batch.Buffer[offset + 1] = color.G;
		batch.Buffer[offset + 2] = color.B;
		batch.Buffer[offset + 3] = color.A;
		batch.IsDirty = true;
	}

	private static void EnsureVisibilityRect(BatchRecord batch)
	{
		if (batch.HasVisibilityRect)
		{
			return;
		}

		batch.VisibilityRect = FixedVisibilityRect;
		batch.HasVisibilityRect = true;
		batch.MultiMesh.CustomAabb = BuildCustomAabb(batch.VisibilityRect);
		RenderingServer.CanvasItemSetCustomRect(batch.Node.GetCanvasItem(), true, batch.VisibilityRect);
	}

	private static void ClearVisibilityRect(BatchRecord batch)
	{
		batch.VisibilityRect = new Rect2(Vector2.Zero, Vector2.Zero);
		batch.HasVisibilityRect = false;
		batch.MultiMesh.CustomAabb = new Aabb(Vector3.Zero, Vector3.Zero);
		RenderingServer.CanvasItemSetCustomRect(batch.Node.GetCanvasItem(), false);
	}

	private static void UploadBatch(BatchRecord batch)
	{
		int uploadSlots = batch.VisibleSlotCount;
		if (uploadSlots <= 0)
		{
			SetMultiMeshInstanceCount(batch, 0);
			batch.IsDirty = false;
			return;
		}

		EnsureUploadBufferCapacity(batch, uploadSlots);
		int uploadFloatCount = uploadSlots * FloatsPerInstance;
		if (uploadFloatCount > 0)
		{
			Array.Copy(batch.Buffer, batch.UploadBuffer, uploadFloatCount);
		}

		SetMultiMeshInstanceCount(batch, batch.UploadSlotCapacity, uploadSlots);
		RenderingServer.MultimeshSetBuffer(batch.MultiMesh.GetRid(), batch.UploadBuffer);
		batch.IsDirty = false;
	}

	private static void EnsureUploadBufferCapacity(BatchRecord batch, int uploadSlots)
	{
		if (uploadSlots <= batch.UploadSlotCapacity
			&& uploadSlots > batch.UploadSlotCapacity / 4)
		{
			return;
		}

		int uploadSlotCapacity = NextUploadSlotCapacity(uploadSlots);
		batch.UploadSlotCapacity = uploadSlotCapacity;
		batch.UploadBuffer = new float[uploadSlotCapacity * FloatsPerInstance];
	}

	private static int NextUploadSlotCapacity(int uploadSlots)
	{
		int capacity = 1;
		while (capacity < uploadSlots)
		{
			capacity *= 2;
		}

		return capacity;
	}

	private static void SetMultiMeshInstanceCount(BatchRecord batch, int instanceCount)
	{
		SetMultiMeshInstanceCount(batch, instanceCount, instanceCount);
	}

	private static void SetMultiMeshInstanceCount(BatchRecord batch, int instanceCount, int visibleInstanceCount)
	{
		if (batch.MultiMesh.InstanceCount != instanceCount)
		{
			batch.MultiMesh.InstanceCount = instanceCount;
		}

		if (batch.MultiMesh.VisibleInstanceCount != visibleInstanceCount)
		{
			batch.MultiMesh.VisibleInstanceCount = visibleInstanceCount;
		}
	}

	private static Aabb BuildCustomAabb(Rect2 rect)
	{
		return new Aabb(
			new Vector3(rect.Position.X, rect.Position.Y, -1.0f),
			new Vector3(rect.Size.X, rect.Size.Y, 2.0f));
	}

	/// <summary>
	/// Mutable render batch state for one projectile type.
	/// </summary>
	private sealed class BatchRecord
	{
		public readonly int TypeId;
		public readonly MultiMeshInstance2D Node;
		public readonly MultiMesh MultiMesh;
		public readonly RenderDefinition Definition;
		public readonly RenderPrepDefinition PrepDefinition;
		public readonly Stack<int> FreeSlots;
		public readonly bool[] OccupiedSlots;
		public readonly Transform2D[] CachedTransforms;
		public readonly float[] Buffer;
		public readonly int Capacity;
		public float[] UploadBuffer;
		public int UploadSlotCapacity;
		public int LiveCount;
		public int VisibleSlotCount;
		public Rect2 VisibilityRect;
		public bool HasVisibilityRect;
		public bool IsDirty;

		public BatchRecord(
			int typeId,
			MultiMeshInstance2D node,
			MultiMesh multiMesh,
			RenderDefinition definition,
			int capacity)
		{
			TypeId = typeId;
			Node = node;
			MultiMesh = multiMesh;
			Definition = definition;
			PrepDefinition = new RenderPrepDefinition(definition);
			Capacity = capacity;
			FreeSlots = new Stack<int>(capacity);
			OccupiedSlots = new bool[capacity];
			CachedTransforms = new Transform2D[capacity];
			Buffer = new float[capacity * FloatsPerInstance];
			UploadBuffer = Array.Empty<float>();
			UploadSlotCapacity = 0;
			LiveCount = 0;
			VisibleSlotCount = 0;
			VisibilityRect = new Rect2(Vector2.Zero, Vector2.Zero);
			HasVisibilityRect = false;
			IsDirty = false;
		}
	}

	private sealed class RenderRangeCommands
	{
		public readonly int StartIndex;
		public readonly List<PreparedRenderCommand> Commands = new(1);

		public RenderRangeCommands(int startIndex)
		{
			StartIndex = startIndex;
		}
	}

	private readonly struct PreparedRenderCommand
	{
		public readonly int TypeId;
		public readonly int Slot;
		public readonly Vector2 XAxis;
		public readonly Vector2 YAxis;
		public readonly Vector2 Origin;

		public PreparedRenderCommand(int typeId, int slot, Vector2 xAxis, Vector2 yAxis, Vector2 origin)
		{
			TypeId = typeId;
			Slot = slot;
			XAxis = xAxis;
			YAxis = yAxis;
			Origin = origin;
		}
	}

	private readonly struct RenderPrepDefinition
	{
		public readonly Vector2 LocalOrigin;
		public readonly float LocalRotationCosine;
		public readonly float LocalRotationSine;
		public readonly Vector2 LocalScale;

		public RenderPrepDefinition(in RenderDefinition definition)
		{
			LocalOrigin = definition.LocalOrigin;
			LocalRotationCosine = definition.LocalRotationCosine;
			LocalRotationSine = definition.LocalRotationSine;
			LocalScale = definition.LocalScale;
		}
	}
}
