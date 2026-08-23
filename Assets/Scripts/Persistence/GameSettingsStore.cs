using System;
using System.IO;
using UnityEngine;

namespace PlayGround.Persistence
{
    public sealed class GameSettingsStore
    {
        private readonly string settingsPath;

        public GameSettingsStore(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("Settings path cannot be empty.", nameof(settingsPath));
            }

            this.settingsPath = settingsPath;
        }

        public string SettingsPath => settingsPath;

        public bool TryLoad(out GameSettingsData data, out string error)
        {
            data = null;
            error = null;
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(settingsPath);
                data = JsonUtility.FromJson<GameSettingsData>(json);
                if (data == null || !data.IsValid())
                {
                    data = null;
                    error = "Settings file is corrupt or uses an unsupported version.";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TrySave(GameSettingsData data, out string error)
        {
            error = null;
            if (data == null || !data.IsValid())
            {
                error = "Refusing to write invalid game settings data.";
                return false;
            }

            string temporaryPath = settingsPath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
                if (File.Exists(settingsPath))
                {
                    File.Replace(temporaryPath, settingsPath, null);
                }
                else
                {
                    File.Move(temporaryPath, settingsPath);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
