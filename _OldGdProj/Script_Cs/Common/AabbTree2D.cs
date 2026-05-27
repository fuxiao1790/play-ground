using Godot;
using System;
using System.Collections.Generic;

namespace PlayGround.Common;

public interface IAabbTree2DVisitor
{
    void Visit(int itemIndex);
}

public sealed class AabbTree2D
{
    private readonly struct Node
    {
        public Node(Rect2 bounds, uint layerMask, int left, int right, int itemIndex)
        {
            Bounds = bounds;
            LayerMask = layerMask;
            Left = left;
            Right = right;
            ItemIndex = itemIndex;
        }

        public Rect2 Bounds { get; }
        public uint LayerMask { get; }
        public int Left { get; }
        public int Right { get; }
        public int ItemIndex { get; }
        public bool IsLeaf => ItemIndex >= 0;
    }

    private readonly List<Node> _nodes = new();
    private readonly List<int> _buildIndices = new();
    private readonly ItemIndexComparer _itemIndexComparer;
    private IReadOnlyList<Rect2>? _sourceBounds;
    private IReadOnlyList<uint>? _sourceLayerMasks;
    private int _root = -1;

    public int NodeCount => _nodes.Count;
    public int MaxDepth { get; private set; }
    public long LastBuildUs { get; private set; }

    public AabbTree2D()
    {
        _itemIndexComparer = new ItemIndexComparer(this);
    }

    public void Clear()
    {
        _nodes.Clear();
        _buildIndices.Clear();
        _sourceBounds = null;
        _sourceLayerMasks = null;
        _root = -1;
        MaxDepth = 0;
        LastBuildUs = 0;
    }

    public void Build(IReadOnlyList<Rect2> bounds, IReadOnlyList<uint> layerMasks)
    {
        ulong phaseStart = Time.GetTicksUsec();
        Clear();
        if (bounds.Count != layerMasks.Count)
        {
            throw new ArgumentException("AabbTree2D requires matching bounds and layer mask counts.");
        }
        if (bounds.Count == 0)
        {
            LastBuildUs = (long)(Time.GetTicksUsec() - phaseStart);
            return;
        }

        _sourceBounds = bounds;
        _sourceLayerMasks = layerMasks;
        for (int i = 0; i < bounds.Count; i++)
        {
            _buildIndices.Add(i);
        }

        _root = BuildNode(0, _buildIndices.Count, 1);
        LastBuildUs = (long)(Time.GetTicksUsec() - phaseStart);
    }

    public int Query(Rect2 bounds, uint layerMask, Action<int> visitItem)
    {
        var visitor = new ActionVisitor(visitItem);
        return Query(bounds, layerMask, ref visitor);
    }

    public int Query<TVisitor>(Rect2 bounds, uint layerMask, ref TVisitor visitor)
        where TVisitor : struct, IAabbTree2DVisitor
    {
        int nodeVisits = 0;
        QueryNode(_root, bounds, layerMask, ref visitor, ref nodeVisits);
        return nodeVisits;
    }

    private int BuildNode(int start, int end, int depth)
    {
        MaxDepth = Mathf.Max(MaxDepth, depth);
        int count = end - start;
        if (count == 1)
        {
            int itemIndex = _buildIndices[start];
            return AddNode(new Node(SourceBounds[itemIndex], SourceLayerMasks[itemIndex], -1, -1, itemIndex));
        }

        Rect2 nodeBounds = SourceBounds[_buildIndices[start]];
        uint layerMask = 0;
        Vector2 centroidMin = BoundsCenter(nodeBounds);
        Vector2 centroidMax = centroidMin;
        for (int i = start; i < end; i++)
        {
            int itemIndex = _buildIndices[i];
            Rect2 itemBounds = SourceBounds[itemIndex];
            nodeBounds = CombineBounds(nodeBounds, itemBounds);
            layerMask |= SourceLayerMasks[itemIndex];
            Vector2 center = BoundsCenter(itemBounds);
            centroidMin = new Vector2(Mathf.Min(centroidMin.X, center.X), Mathf.Min(centroidMin.Y, center.Y));
            centroidMax = new Vector2(Mathf.Max(centroidMax.X, center.X), Mathf.Max(centroidMax.Y, center.Y));
        }

        Vector2 centroidExtent = centroidMax - centroidMin;
        _itemIndexComparer.Axis = centroidExtent.X >= centroidExtent.Y ? 0 : 1;
        _buildIndices.Sort(start, count, _itemIndexComparer);

        int middle = start + (count / 2);
        int left = BuildNode(start, middle, depth + 1);
        int right = BuildNode(middle, end, depth + 1);
        Node leftNode = _nodes[left];
        Node rightNode = _nodes[right];
        return AddNode(new Node(
            CombineBounds(leftNode.Bounds, rightNode.Bounds),
            leftNode.LayerMask | rightNode.LayerMask,
            left,
            right,
            -1));
    }

    private int AddNode(Node node)
    {
        int index = _nodes.Count;
        _nodes.Add(node);
        return index;
    }

    private void QueryNode<TVisitor>(int nodeIndex, Rect2 bounds, uint layerMask, ref TVisitor visitor, ref int nodeVisits)
        where TVisitor : struct, IAabbTree2DVisitor
    {
        if (nodeIndex < 0)
        {
            return;
        }

        nodeVisits++;
        Node node = _nodes[nodeIndex];
        if (!MaskMatches(layerMask, node.LayerMask) || !bounds.Intersects(node.Bounds, includeBorders: true))
        {
            return;
        }

        if (node.IsLeaf)
        {
            visitor.Visit(node.ItemIndex);
            return;
        }

        QueryNode(node.Left, bounds, layerMask, ref visitor, ref nodeVisits);
        QueryNode(node.Right, bounds, layerMask, ref visitor, ref nodeVisits);
    }

    private IReadOnlyList<Rect2> SourceBounds => _sourceBounds ?? throw new InvalidOperationException("AabbTree2D has no source bounds.");
    private IReadOnlyList<uint> SourceLayerMasks => _sourceLayerMasks ?? throw new InvalidOperationException("AabbTree2D has no source layer masks.");

    private static bool MaskMatches(uint mask, uint layer)
    {
        return (mask & layer) != 0;
    }

    private static Vector2 BoundsCenter(Rect2 bounds)
    {
        return bounds.Position + (bounds.Size * 0.5f);
    }

    private static Rect2 CombineBounds(Rect2 a, Rect2 b)
    {
        Vector2 aEnd = a.Position + a.Size;
        Vector2 bEnd = b.Position + b.Size;
        float minX = Mathf.Min(a.Position.X, b.Position.X);
        float minY = Mathf.Min(a.Position.Y, b.Position.Y);
        float maxX = Mathf.Max(aEnd.X, bEnd.X);
        float maxY = Mathf.Max(aEnd.Y, bEnd.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private sealed class ItemIndexComparer : IComparer<int>
    {
        private readonly AabbTree2D _tree;

        public ItemIndexComparer(AabbTree2D tree)
        {
            _tree = tree;
        }

        public int Axis { get; set; }

        public int Compare(int left, int right)
        {
            Vector2 leftCenter = BoundsCenter(_tree.SourceBounds[left]);
            Vector2 rightCenter = BoundsCenter(_tree.SourceBounds[right]);
            float leftValue = Axis == 0 ? leftCenter.X : leftCenter.Y;
            float rightValue = Axis == 0 ? rightCenter.X : rightCenter.Y;
            int valueComparison = leftValue.CompareTo(rightValue);
            return valueComparison != 0 ? valueComparison : left.CompareTo(right);
        }
    }

    private readonly struct ActionVisitor : IAabbTree2DVisitor
    {
        private readonly Action<int> _visitItem;

        public ActionVisitor(Action<int> visitItem)
        {
            _visitItem = visitItem;
        }

        public void Visit(int itemIndex)
        {
            _visitItem(itemIndex);
        }
    }
}
