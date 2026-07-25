using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    // ECS Lifecycle: seeded once at proxy creation from the unit stat sheet.
    // GameObject owns Max and RegenPerSecond; ECS owns Current until proxy destruction.
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
        public float RegenPerSecond;
    }

    // ECS Lifecycle: seeded once at proxy creation from the unit stat sheet.
    // GameObject owns Max and RegenPerSecond; ECS owns Current until proxy destruction.
    public struct Mana : IComponentData
    {
        public float Current;
        public float Max;
        public float RegenPerSecond;
    }
}
