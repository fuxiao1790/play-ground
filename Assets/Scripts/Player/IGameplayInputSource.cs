namespace PlayGround.Player
{
    // Supplies per-frame gameplay pointer intent to PlayerRoot.
    // Implemented by the UI layer (world click surface) and registered into
    // PlayerRoot; PlayerRoot never reads the raw pointer button itself.
    public interface IGameplayInputSource
    {
        // True while the player is holding fire over the world (not over UI).
        bool FireHeld { get; }
    }
}
