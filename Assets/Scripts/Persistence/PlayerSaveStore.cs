using System;
using System.IO;
using UnityEngine;

namespace PlayGround.Persistence
{
    public sealed class PlayerSaveStore
    {
        private readonly string savePath;

        public PlayerSaveStore(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("Save path cannot be empty.", nameof(savePath));
            }

            this.savePath = savePath;
        }

        public string SavePath => savePath;

        public bool TryLoad(out PlayerSaveData data, out string error)
        {
            data = null;
            error = null;

            if (!File.Exists(savePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(savePath);
                data = JsonUtility.FromJson<PlayerSaveData>(json);
                if (data == null || !data.IsValid())
                {
                    data = null;
                    error = "Save file is corrupt or uses an unsupported version.";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TrySave(PlayerSaveData data, out string error)
        {
            error = null;
            if (data == null || !data.IsValid())
            {
                error = "Refusing to write invalid player save data.";
                return false;
            }

            string temporaryPath = savePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
                if (File.Exists(savePath))
                {
                    File.Replace(temporaryPath, savePath, null);
                }
                else
                {
                    File.Move(temporaryPath, savePath);
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
