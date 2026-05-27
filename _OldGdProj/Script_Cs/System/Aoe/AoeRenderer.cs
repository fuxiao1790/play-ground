using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Godot-facing AOE renderer that stores one MultiMesh batch per AOE type.
/// </summary>
public partial class AoeRenderer : MultiMeshInstance2D
{
	private static readonly Vector2 HiddenOrigin = new(-1000000.0f, -1000000.0f);
	private static readonly Color HiddenColor = new(1.0f, 1.0f, 1.0f, 0.0f);
	private static readonly Mesh UnitQuadMesh = CreateUnitQuadMesh();
	private static readonly Rect2 FixedVisibilityRect = new(
		new Vector2(-1000000.0f, -1000000.0f),
		new Vector2(2000000.0f, 2000000.0f));
	private const int AoeZIndex = 45;
	private const int TransformFloatCount2D = 8;
	private const int ColorFloatCount = 4;
	private const int FloatsPerInstance = TransformFloatCount2D + ColorFloatCount;

	private readonly Dictionary<int, BatchRecord> _batches = new();
	private readonly Dictionary<EntityHandle, RenderSlot> _slotsByAoe = new();
	private int _nextBatchNodeId;

	public override void _Ready()
	{
		ConfigureBatchNode(this);
	}

	public void RegisterAoeType(int typeId, in AoeRenderDefinition definition, int maxInstances)
	{
		if (!definition.HasVisual || _batches.ContainsKey(typeId))
		{
			return;
		}

		if (maxInstances <= 0)
		{
			throw new InvalidOperationException("AOE renderer requires a positive AOE capacity.");
		}

		MultiMeshInstance2D node = _batches.Count == 0 ? this : CreateBatchNode(typeId);
		ConfigureBatchNode(node);
		MultiMesh multiMesh = CreateMultiMesh();
		node.Texture = definition.Texture;
		node.Multimesh = multiMesh;

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

	public void OnSpawn(EntityHandle aoe, int typeId, Vector2 position)
	{
		if (!_batches.TryGetValue(typeId, out BatchRecord? batch))
		{
			return;
		}

		if (batch.FreeSlots.Count == 0)
		{
			throw new InvalidOperationException($"AOE render batch {typeId} is full.");
		}

		int slot = batch.FreeSlots.Pop();
		_slotsByAoe[aoe] = new RenderSlot(typeId, slot);
		batch.LiveCount++;
		batch.OccupiedSlots[slot] = true;
		if (slot + 1 > batch.VisibleSlotCount)
		{
			batch.VisibleSlotCount = slot + 1;
		}

		SetColor(batch, slot, batch.Definition.Modulate);
		EnsureVisibilityRect(batch);
		SetTransform(batch, slot, position);
	}

	public void OnDespawn(EntityHandle aoe)
	{
		if (!_slotsByAoe.TryGetValue(aoe, out RenderSlot renderSlot))
		{
			return;
		}

		_slotsByAoe.Remove(aoe);
		if (!_batches.TryGetValue(renderSlot.TypeId, out BatchRecord? batch))
		{
			return;
		}

		int slot = renderSlot.Slot;
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
		_slotsByAoe.Clear();
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

	private MultiMeshInstance2D CreateBatchNode(int typeId)
	{
		Node? parent = GetParent();
		if (parent == null)
		{
			throw new InvalidOperationException("AOE renderer needs a parent node before registering AOE types.");
		}

		var node = new MultiMeshInstance2D
		{
			Name = $"AoeMultiMeshInstance2D_Batch_{typeId}_{_nextBatchNodeId++}",
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
		node.ZIndex = AoeZIndex;
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

	private static void SetHidden(BatchRecord batch, int slot)
	{
		SetTransform(batch, slot, HiddenOrigin);
		SetColor(batch, slot, HiddenColor);
	}

	private static void SetTransform(BatchRecord batch, int slot, Vector2 position)
	{
		float cosine = batch.Definition.LocalRotationCosine;
		float sine = batch.Definition.LocalRotationSine;
		Vector2 origin = position + batch.Definition.LocalOrigin;
		Vector2 xAxis = new(
			batch.Definition.LocalScale.X * cosine,
			batch.Definition.LocalScale.X * sine);
		Vector2 yAxis = new(
			-batch.Definition.LocalScale.Y * sine,
			batch.Definition.LocalScale.Y * cosine);

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

	private sealed class BatchRecord
	{
		public readonly int TypeId;
		public readonly MultiMeshInstance2D Node;
		public readonly MultiMesh MultiMesh;
		public readonly AoeRenderDefinition Definition;
		public readonly Stack<int> FreeSlots;
		public readonly bool[] OccupiedSlots;
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
			in AoeRenderDefinition definition,
			int capacity)
		{
			TypeId = typeId;
			Node = node;
			MultiMesh = multiMesh;
			Definition = definition;
			Capacity = capacity;
			FreeSlots = new Stack<int>(capacity);
			OccupiedSlots = new bool[capacity];
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

	private readonly struct RenderSlot
	{
		public readonly int TypeId;
		public readonly int Slot;

		public RenderSlot(int typeId, int slot)
		{
			TypeId = typeId;
			Slot = slot;
		}
	}
}

/// <summary>
/// Scene-baked render data used by an AOE MultiMesh batch.
/// </summary>
public readonly struct AoeRenderDefinition
{
	public readonly Texture2D? Texture;
	public readonly Vector2 LocalOrigin;
	public readonly float LocalRotationCosine;
	public readonly float LocalRotationSine;
	public readonly Vector2 LocalScale;
	public readonly Color Modulate;

	public bool HasVisual => Texture != null;

	public AoeRenderDefinition(
		Texture2D? texture,
		Vector2 localOrigin,
		float localRotation,
		Vector2 localScale,
		Color modulate)
	{
		Texture = texture;
		LocalOrigin = localOrigin;
		LocalRotationCosine = Mathf.Cos(localRotation);
		LocalRotationSine = Mathf.Sin(localRotation);
		LocalScale = localScale;
		Modulate = modulate;
	}

	public static AoeRenderDefinition None()
	{
		return new AoeRenderDefinition(null, Vector2.Zero, 0.0f, Vector2.One, Colors.Transparent);
	}
}

/// <summary>
/// Bakes an AOE effect scene's Sprite2D data into a reusable render definition.
/// </summary>
public static class AoeRenderDefinitionBaker
{
	private static readonly NodePath SpritePath = new("Sprite2D");

	public static AoeRenderDefinition FromTemplate(PackedScene template)
	{
		Node instance = template.Instantiate<Node>();
		try
		{
			Sprite2D? sprite = instance.GetNodeOrNull<Sprite2D>(SpritePath);
			if (sprite == null)
			{
				return AoeRenderDefinition.None();
			}

			if (sprite.Texture == null)
			{
				return AoeRenderDefinition.None();
			}

			Vector2 localOrigin = sprite.Position + sprite.Offset;
			Vector2 quadScale = sprite.Texture.GetSize() * sprite.Scale;
			if (!sprite.Centered)
			{
				localOrigin += new Vector2(
					quadScale.X * 0.5f,
					quadScale.Y * 0.5f);
			}

			return new AoeRenderDefinition(
				sprite.Texture,
				localOrigin,
				sprite.Rotation,
				quadScale,
				sprite.Modulate);
		}
		finally
		{
			instance.Free();
		}
	}
}
