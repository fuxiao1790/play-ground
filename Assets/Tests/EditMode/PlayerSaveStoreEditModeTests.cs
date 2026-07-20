using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Persistence;
using PlayGround.Skills;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class PlayerSaveStoreEditModeTests
    {
        private string temporaryDirectory;
        private string savePath;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "PlayGroundPlayerSaveTests",
                Guid.NewGuid().ToString("N"));
            savePath = Path.Combine(temporaryDirectory, "player-save.json");
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
        public void SaveThenLoadRoundTripsPlayerAndGuidLoadoutData()
        {
            var data = new PlayerSaveData
            {
                player = new PlayerStateSaveData
                {
                    positionX = 12.5f,
                    positionY = -4.25f,
                    currentHealth = 73f
                },
                skillLoadout = new PlayerSkillLoadoutSaveData
                {
                    nodes = new List<PlayerSkillNodeSaveData>
                    {
                        new()
                        {
                            skillAssetGuid = "0123456789abcdef0123456789abcdef",
                            supportAssetGuids = new List<string>
                            {
                                "abcdef0123456789abcdef0123456789",
                                string.Empty
                            },
                            triggerToNextAssetGuid = "11111111111111111111111111111111"
                        }
                    }
                }
            };
            var store = new PlayerSaveStore(savePath);

            Assert.That(store.TrySave(data, out string saveError), Is.True, saveError);
            Assert.That(store.TryLoad(out PlayerSaveData loaded, out string loadError), Is.True, loadError);
            Assert.That(loaded.version, Is.EqualTo(PlayerSaveData.CurrentVersion));
            Assert.That(loaded.player.positionX, Is.EqualTo(12.5f));
            Assert.That(loaded.player.positionY, Is.EqualTo(-4.25f));
            Assert.That(loaded.player.currentHealth, Is.EqualTo(73f));
            Assert.That(loaded.skillLoadout.nodes[0].skillAssetGuid, Is.EqualTo(data.skillLoadout.nodes[0].skillAssetGuid));
            Assert.That(loaded.skillLoadout.nodes[0].supportAssetGuids, Is.EqualTo(data.skillLoadout.nodes[0].supportAssetGuids));
            Assert.That(loaded.skillLoadout.nodes[0].triggerToNextAssetGuid, Is.EqualTo(data.skillLoadout.nodes[0].triggerToNextAssetGuid));
        }

        [Test]
        public void CorruptSaveIsRejectedWithoutThrowing()
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllText(savePath, "{ definitely not valid json");
            var store = new PlayerSaveStore(savePath);

            Assert.That(store.TryLoad(out PlayerSaveData loaded, out string error), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void SkillAssetCarriesItsUnityAssetGuid()
        {
            const string path = "Assets/ScriptableObjects/Player/Skills/Skill/MagicBolt.asset";
            Skill skill = AssetDatabase.LoadAssetAtPath<Skill>(path);

            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.AssetGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(path)));
        }

        [Test]
        public void SkillDriverRestoresRuntimeNodesAndAdvancesRevision()
        {
            var gameObject = new GameObject("SkillDriver restore test");
            try
            {
                SkillDriver driver = gameObject.AddComponent<SkillDriver>();
                MethodInfo start = typeof(SkillDriver).GetMethod(
                    "Start",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(start, Is.Not.Null);
                start.Invoke(driver, null);

                var restored = new List<SkillLoadoutRestoreNode>
                {
                    new(null, Array.Empty<SkillSupport>(), null),
                    new(null, Array.Empty<SkillSupport>(), null)
                };

                Assert.That(driver.TryRestoreRuntimeLoadout(restored, out string error), Is.True, error);
                Assert.That(driver.RuntimeNodes.Count, Is.EqualTo(2));
                Assert.That(driver.Revision, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
