using System;
using System.IO;
using NUnit.Framework;
using PlayGround.Persistence;

namespace PlayGround.Tests.EditMode
{
    public sealed class GameSettingsStoreEditModeTests
    {
        private string temporaryDirectory;
        private string settingsPath;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "PlayGroundGameSettingsTests",
                Guid.NewGuid().ToString("N"));
            settingsPath = Path.Combine(temporaryDirectory, "game-settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }

        [Test]
        public void NewDataDisplaysMobHealthBarsByDefault()
        {
            var data = new GameSettingsData();

            Assert.That(data.displayMobHealthBars, Is.True);
            Assert.That(data.IsValid(), Is.True);
        }

        [Test]
        public void SaveThenLoadRoundTripsDisplayMobHealthBars()
        {
            var data = new GameSettingsData { displayMobHealthBars = false };
            var store = new GameSettingsStore(settingsPath);

            Assert.That(store.TrySave(data, out string saveError), Is.True, saveError);
            Assert.That(store.TryLoad(out GameSettingsData loaded, out string loadError), Is.True, loadError);
            Assert.That(loaded.version, Is.EqualTo(GameSettingsData.CurrentVersion));
            Assert.That(loaded.displayMobHealthBars, Is.False);
        }

        [Test]
        public void CorruptSettingsAreRejectedWithoutThrowing()
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllText(settingsPath, "{ definitely not valid json");
            var store = new GameSettingsStore(settingsPath);

            Assert.That(store.TryLoad(out GameSettingsData loaded, out string error), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void UnsupportedVersionIsRejected()
        {
            var data = new GameSettingsData { version = GameSettingsData.CurrentVersion + 1 };
            var store = new GameSettingsStore(settingsPath);

            Assert.That(store.TrySave(data, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }
    }
}
