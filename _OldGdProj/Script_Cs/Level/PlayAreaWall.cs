using Godot;
using System;

namespace PlayGround.Level;

[GlobalClass]
public partial class PlayAreaWall : Node2D
{
    [Export] public Vector2 bounds_size = new(1280.0f, 720.0f);
    [Export] public int tile_size = 16;
    [Export(PropertyHint.Layers2DPhysics)] public uint collision_layer = 2;

    [Export] public Texture2D top_left_tile = null!;
    [Export] public Texture2D top_tile = null!;
    [Export] public Texture2D top_right_tile = null!;
    [Export] public Texture2D left_tile = null!;
    [Export] public Texture2D center_tile = null!;
    [Export] public Texture2D right_tile = null!;
    [Export] public Texture2D bottom_left_tile = null!;
    [Export] public Texture2D bottom_tile = null!;
    [Export] public Texture2D bottom_right_tile = null!;

    private StaticBody2D _topBody = null!;
    private StaticBody2D _bottomBody = null!;
    private StaticBody2D _leftBody = null!;
    private StaticBody2D _rightBody = null!;

    public override void _Ready()
    {
        _topBody = GetNode<StaticBody2D>("TopWall");
        _bottomBody = GetNode<StaticBody2D>("BottomWall");
        _leftBody = GetNode<StaticBody2D>("LeftWall");
        _rightBody = GetNode<StaticBody2D>("RightWall");

        ValidateConfigurationOrThrow();

        ConfigureCollision();
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2I tileCount = TileCount();
        Vector2 size = new(tile_size, tile_size);
        int lastX = tileCount.X - 1;
        int lastY = tileCount.Y - 1;

        DrawTile(top_left_tile, Vector2.Zero, size);
        DrawTile(top_right_tile, new Vector2(lastX * tile_size, 0.0f), size);
        DrawTile(bottom_left_tile, new Vector2(0.0f, lastY * tile_size), size);
        DrawTile(bottom_right_tile, new Vector2(lastX * tile_size, lastY * tile_size), size);

        for (int x = 1; x < lastX; x++)
        {
            DrawTile(top_tile, new Vector2(x * tile_size, 0.0f), size);
            DrawTile(bottom_tile, new Vector2(x * tile_size, lastY * tile_size), size);
        }

        for (int y = 1; y < lastY; y++)
        {
            DrawTile(left_tile, new Vector2(0.0f, y * tile_size), size);
            DrawTile(right_tile, new Vector2(lastX * tile_size, y * tile_size), size);
        }
    }

    private void ConfigureCollision()
    {
        ConfigureWallBody(_topBody, new Vector2(bounds_size.X * 0.5f, tile_size * 0.5f), new Vector2(bounds_size.X, tile_size));
        ConfigureWallBody(_bottomBody, new Vector2(bounds_size.X * 0.5f, bounds_size.Y - tile_size * 0.5f), new Vector2(bounds_size.X, tile_size));
        ConfigureWallBody(_leftBody, new Vector2(tile_size * 0.5f, bounds_size.Y * 0.5f), new Vector2(tile_size, bounds_size.Y));
        ConfigureWallBody(_rightBody, new Vector2(bounds_size.X - tile_size * 0.5f, bounds_size.Y * 0.5f), new Vector2(tile_size, bounds_size.Y));
    }

    private void ConfigureWallBody(StaticBody2D body, Vector2 bodyPosition, Vector2 shapeSize)
    {
        body.Position = bodyPosition;
        body.CollisionLayer = collision_layer;
        body.CollisionMask = 0;

        CollisionShape2D collisionShape = body.GetNode<CollisionShape2D>("CollisionShape2D");
        collisionShape.Shape = new RectangleShape2D { Size = shapeSize };
    }

    private void DrawTile(Texture2D texture, Vector2 tilePosition, Vector2 tileExtents) =>
        DrawTextureRect(texture, new Rect2(tilePosition, tileExtents), false);

    private Vector2I TileCount()
    {
        return new Vector2I(
            (int)(bounds_size.X / tile_size),
            (int)(bounds_size.Y / tile_size));
    }

    private void ValidateConfigurationOrThrow()
    {
        if (tile_size <= 0)
        {
            throw new InvalidOperationException("PlayAreaWall tile_size must be greater than 0.");
        }

        if (bounds_size.X < tile_size * 3 || bounds_size.Y < tile_size * 3)
        {
            throw new InvalidOperationException("PlayAreaWall bounds_size must be at least 3 tiles wide and tall.");
        }

        if (tile_size > 0 && (!Mathf.IsZeroApprox(bounds_size.X % tile_size) || !Mathf.IsZeroApprox(bounds_size.Y % tile_size)))
        {
            throw new InvalidOperationException("PlayAreaWall bounds_size must be a multiple of tile_size.");
        }

        if (!HasRequiredTiles())
        {
            throw new InvalidOperationException("PlayAreaWall needs all nine wall tile textures assigned.");
        }
    }

    private bool HasRequiredTiles()
    {
        return top_left_tile != null
            && top_tile != null
            && top_right_tile != null
            && left_tile != null
            && center_tile != null
            && right_tile != null
            && bottom_left_tile != null
            && bottom_tile != null
            && bottom_right_tile != null;
    }

}
