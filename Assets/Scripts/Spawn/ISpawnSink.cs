namespace PlayGround.Spawn
{
    public interface ISpawnSink
    {
        int ActiveCount { get; }
        int Cap { get; }
        bool CanSpawn { get; }
        void Spawn();
    }
}
