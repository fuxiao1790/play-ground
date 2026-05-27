using Godot;
using System;

/// <summary>
/// Scene-baked render data used by a projectile MultiMesh batch.
/// </summary>
public readonly struct RenderDefinition
{
	public readonly Texture2D Texture;
	public readonly Vector2 LocalOrigin;
	public readonly float LocalRotation;
	public readonly float LocalRotationCosine;
	public readonly float LocalRotationSine;
	public readonly Vector2 LocalScale;
	public readonly Color Modulate;

	public RenderDefinition(
		Texture2D texture,
		Vector2 localOrigin,
		float localRotation,
		Vector2 localScale,
		Color modulate)
	{
		Texture = texture;
		LocalOrigin = localOrigin;
		LocalRotation = localRotation;
		LocalScale = localScale;
		Modulate = modulate;
		LocalRotationCosine = Mathf.Cos(localRotation);
		LocalRotationSine = Mathf.Sin(localRotation);
	}
}

/// <summary>
/// Bakes a projectile scene's Sprite2D data into a reusable render definition.
/// </summary>
public static class RenderDefinitionBaker
{
	private static readonly NodePath SpritePath = new("Sprite2D");

	public static RenderDefinition FromTemplate(PackedScene template)
	{
		Node instance = template.Instantiate<Node>();
		try
		{
			Sprite2D? sprite = instance.GetNodeOrNull<Sprite2D>(SpritePath);
			if (sprite == null)
			{
				throw new InvalidOperationException("Projectile template needs a Sprite2D child at 'Sprite2D'.");
			}

			if (sprite.Texture == null)
			{
				throw new InvalidOperationException("Projectile Sprite2D needs a texture before render baking.");
			}

			Vector2 localOrigin = sprite.Position + sprite.Offset;
			Vector2 quadScale = sprite.Texture.GetSize() * sprite.Scale;
			if (!sprite.Centered)
			{
				localOrigin += new Vector2(
					quadScale.X * 0.5f,
					quadScale.Y * 0.5f);
			}

			return new RenderDefinition(
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
