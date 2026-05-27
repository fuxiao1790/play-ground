using Godot;

namespace PlayGround.Spawn;

[GlobalClass]
public partial class SpawnConfig : Resource
{
    [Export] public Godot.Collections.Array<PackedScene> mob_scenes = new();
}
