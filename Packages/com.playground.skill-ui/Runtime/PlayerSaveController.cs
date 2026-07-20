using System;
using System.Collections.Generic;
using System.IO;
using PlayGround.Common;
using PlayGround.Persistence;
using PlayGround.Player;
using UnityEngine;

namespace PlayGround.Skills
{
    [DefaultExecutionOrder(100)]
    public sealed class PlayerSaveController : MonoBehaviour
    {
        private const string SaveFileName = "player-save.json";

        [SerializeField] private PlayerRoot playerRoot;
        [SerializeField] private SkillDriver skillDriver;
        [SerializeField] private SkillUiCatalog catalog;
        [SerializeField, Min(1f)] private float autosaveSeconds = 30f;

        private PlayerSaveStore store;
        private float nextAutosaveTime;
        private bool initialized;

        public string SavePath => store?.SavePath;

        private void Awake()
        {
            if (playerRoot == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerSaveController)} on {name} needs a {nameof(PlayerRoot)}.");
            }

            if (skillDriver == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerSaveController)} on {name} needs a {nameof(SkillDriver)}.");
            }

            if (catalog == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerSaveController)} on {name} needs a {nameof(SkillUiCatalog)}.");
            }

            store = new PlayerSaveStore(Path.Combine(Application.persistentDataPath, SaveFileName));
        }

        private void Start()
        {
            if (!TryLoadNow(out string error) && !string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"Player save was not loaded: {error}", this);
            }

            initialized = true;
            ScheduleNextAutosave();
        }

        private void OnEnable()
        {
            skillDriver.LoadoutChanged += OnLoadoutChanged;
        }

        private void OnDisable()
        {
            skillDriver.LoadoutChanged -= OnLoadoutChanged;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAutosaveTime)
            {
                return;
            }

            SaveAndReport();
            ScheduleNextAutosave();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && initialized)
            {
                SaveAndReport();
            }
        }

        private void OnApplicationQuit()
        {
            if (initialized)
            {
                SaveAndReport();
            }
        }

        public bool TrySaveNow(out string error)
        {
            if (!TryCapture(out PlayerSaveData data, out error))
            {
                return false;
            }

            return store.TrySave(data, out error);
        }

        public bool TryLoadNow(out string error)
        {
            if (!store.TryLoad(out PlayerSaveData data, out error))
            {
                return false;
            }

            if (!playerRoot.RestorePersistentState(data.player))
            {
                error = "Saved player state is invalid.";
                return false;
            }

            if (data.skillLoadout == null)
            {
                return true;
            }

            if (!TryResolveLoadout(data.skillLoadout, out List<SkillLoadoutRestoreNode> nodes, out error))
            {
                return false;
            }

            return skillDriver.TryRestoreRuntimeLoadout(nodes, out error);
        }

        private bool TryCapture(out PlayerSaveData data, out string error)
        {
            data = new PlayerSaveData
            {
                player = playerRoot.CapturePersistentState()
            };

            IReadOnlyList<SkillLoadoutNode> runtimeNodes = skillDriver.RuntimeNodes;
            if (runtimeNodes == null)
            {
                error = "Runtime skill loadout is not initialized.";
                return false;
            }

            data.skillLoadout.nodes = new List<PlayerSkillNodeSaveData>(runtimeNodes.Count);
            for (int nodeIndex = 0; nodeIndex < runtimeNodes.Count; nodeIndex++)
            {
                SkillLoadoutNode node = runtimeNodes[nodeIndex];
                var savedNode = new PlayerSkillNodeSaveData();
                if (node?.SkillSet != null)
                {
                    if (!TryGuid(node.SkillSet.Skill, $"skill at node {nodeIndex}", out savedNode.skillAssetGuid, out error))
                    {
                        return false;
                    }

                    SkillSupport[] supports = node.SkillSet.Supports;
                    savedNode.supportAssetGuids = new List<string>(supports.Length);
                    for (int supportIndex = 0; supportIndex < supports.Length; supportIndex++)
                    {
                        if (!TryGuid(supports[supportIndex], $"support {supportIndex} at node {nodeIndex}", out string guid, out error))
                        {
                            return false;
                        }

                        savedNode.supportAssetGuids.Add(guid);
                    }
                }

                if (!TryGuid(node?.TriggerToNext, $"trigger at node {nodeIndex}", out savedNode.triggerToNextAssetGuid, out error))
                {
                    return false;
                }

                data.skillLoadout.nodes.Add(savedNode);
            }

            error = null;
            return true;
        }

        private bool TryResolveLoadout(
            PlayerSkillLoadoutSaveData savedLoadout,
            out List<SkillLoadoutRestoreNode> restoredNodes,
            out string error)
        {
            var skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
            var supports = new Dictionary<string, SkillSupport>(StringComparer.OrdinalIgnoreCase);
            var triggers = new Dictionary<string, TriggerLink>(StringComparer.OrdinalIgnoreCase);
            AddCurrentLoadoutAssets(skills, supports, triggers);
            AddCatalogAssets(skills, supports, triggers);

            restoredNodes = new List<SkillLoadoutRestoreNode>(savedLoadout.nodes.Count);
            for (int nodeIndex = 0; nodeIndex < savedLoadout.nodes.Count; nodeIndex++)
            {
                PlayerSkillNodeSaveData savedNode = savedLoadout.nodes[nodeIndex];
                if (!TryResolve(skills, savedNode.skillAssetGuid, $"skill at node {nodeIndex}", out Skill skill, out error)
                    || !TryResolve(triggers, savedNode.triggerToNextAssetGuid, $"trigger at node {nodeIndex}", out TriggerLink trigger, out error))
                {
                    return false;
                }

                var restoredSupports = new SkillSupport[savedNode.supportAssetGuids.Count];
                for (int supportIndex = 0; supportIndex < restoredSupports.Length; supportIndex++)
                {
                    if (!TryResolve(
                        supports,
                        savedNode.supportAssetGuids[supportIndex],
                        $"support {supportIndex} at node {nodeIndex}",
                        out restoredSupports[supportIndex],
                        out error))
                    {
                        return false;
                    }
                }

                restoredNodes.Add(new SkillLoadoutRestoreNode(skill, restoredSupports, trigger));
            }

            error = null;
            return true;
        }

        private void AddCurrentLoadoutAssets(
            Dictionary<string, Skill> skills,
            Dictionary<string, SkillSupport> supports,
            Dictionary<string, TriggerLink> triggers)
        {
            IReadOnlyList<SkillLoadoutNode> nodes = skillDriver.RuntimeNodes;
            if (nodes == null)
            {
                return;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                SkillLoadoutNode node = nodes[i];
                AddAsset(skills, node?.SkillSet?.Skill);
                AddAsset(triggers, node?.TriggerToNext);
                SkillSupport[] nodeSupports = node?.SkillSet?.Supports;
                if (nodeSupports == null)
                {
                    continue;
                }

                for (int supportIndex = 0; supportIndex < nodeSupports.Length; supportIndex++)
                {
                    AddAsset(supports, nodeSupports[supportIndex]);
                }
            }
        }

        private void AddCatalogAssets(
            Dictionary<string, Skill> skills,
            Dictionary<string, SkillSupport> supports,
            Dictionary<string, TriggerLink> triggers)
        {
            for (int i = 0; i < catalog.Skills.Count; i++)
            {
                AddAsset(skills, catalog.Skills[i]?.Definition);
            }

            for (int i = 0; i < catalog.Supports.Count; i++)
            {
                AddAsset(supports, catalog.Supports[i]?.Definition);
            }

            for (int i = 0; i < catalog.Triggers.Count; i++)
            {
                AddAsset(triggers, catalog.Triggers[i]?.Definition);
            }
        }

        private static void AddAsset<T>(Dictionary<string, T> assets, T asset)
            where T : PersistentScriptableObject
        {
            if (asset != null && !string.IsNullOrEmpty(asset.AssetGuid))
            {
                assets[asset.AssetGuid] = asset;
            }
        }

        private static bool TryGuid<T>(T asset, string description, out string guid, out string error)
            where T : PersistentScriptableObject
        {
            if (asset == null)
            {
                guid = string.Empty;
                error = null;
                return true;
            }

            guid = asset.AssetGuid;
            if (!string.IsNullOrEmpty(guid))
            {
                error = null;
                return true;
            }

            error = $"Cannot save {description} because its asset GUID was not baked.";
            return false;
        }

        private static bool TryResolve<T>(
            Dictionary<string, T> assets,
            string guid,
            string description,
            out T asset,
            out string error)
            where T : PersistentScriptableObject
        {
            if (string.IsNullOrEmpty(guid))
            {
                asset = null;
                error = null;
                return true;
            }

            if (assets.TryGetValue(guid, out asset))
            {
                error = null;
                return true;
            }

            error = $"Saved {description} references missing asset GUID {guid}.";
            return false;
        }

        private void OnLoadoutChanged(ulong _)
        {
            SaveAndReport();
            ScheduleNextAutosave();
        }

        private void SaveAndReport()
        {
            if (!TrySaveNow(out string error))
            {
                Debug.LogWarning($"Player save failed: {error}", this);
            }
        }

        private void ScheduleNextAutosave()
        {
            nextAutosaveTime = Time.unscaledTime + Mathf.Max(1f, autosaveSeconds);
        }
    }
}
