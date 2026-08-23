using System;
using System.IO;
using PlayGround.Persistence;
using UnityEngine;

namespace PlayGround.Game
{
    public sealed class GameSettings : MonoBehaviour
    {
        private const string SettingsFileName = "game-settings.json";

        private GameSettingsStore store;
        private GameSettingsData data;
        private bool initialized;

        public event Action<bool> DisplayMobHealthBarsChanged;

        public bool DisplayMobHealthBars
        {
            get
            {
                EnsureInitialized();
                return data.displayMobHealthBars;
            }
        }

        public string SettingsPath
        {
            get
            {
                EnsureInitialized();
                return store.SettingsPath;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public void SetDisplayMobHealthBars(bool display)
        {
            EnsureInitialized();
            if (data.displayMobHealthBars == display)
            {
                return;
            }

            data.displayMobHealthBars = display;
            if (!store.TrySave(data, out string error))
            {
                Debug.LogWarning($"Game settings failed to save: {error}", this);
            }

            DisplayMobHealthBarsChanged?.Invoke(display);
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            store = new GameSettingsStore(Path.Combine(Application.persistentDataPath, SettingsFileName));
            if (!store.TryLoad(out data, out string error))
            {
                data = new GameSettingsData();
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"Game settings were not loaded: {error}", this);
                }
            }

            initialized = true;
        }
    }
}
