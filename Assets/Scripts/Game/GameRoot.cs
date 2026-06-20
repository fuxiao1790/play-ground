using PlayGround.CameraSystem;
using PlayGround.Common;
using PlayGround.Level;
using PlayGround.Mob;
using PlayGround.Spawn;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.Game
{
    public sealed class GameRoot : MonoBehaviour
    {
        [SerializeField] private CombatRoot playerCombatRoot;
        [SerializeField] private CombatRoot mobCombatRoot;
        [SerializeField] private MobSpawnerRoot mobSpawner;
        [SerializeField] private MobRoot[] mobs;
        [SerializeField] private PlayGround.Player.PlayerRoot player;
        [SerializeField] private GameplayCamera gameplayCamera;
        [SerializeField] private PlayAreaRoot playArea;

        private void Awake()
        {
            if (playerCombatRoot == null)
            {
                playerCombatRoot = FindTaggedComponent<CombatRoot>(GameplayTags.PlayerProjectileRoot);
            }

            if (playerCombatRoot == null)
            {
                throw new MissingReferenceException($"{nameof(GameRoot)} needs a player combat root.");
            }

            if (mobCombatRoot == null)
            {
                mobCombatRoot = FindTaggedComponent<CombatRoot>(GameplayTags.MobProjectileRoot);
            }

            if (player == null)
            {
                player = FindTaggedComponent<PlayGround.Player.PlayerRoot>(GameplayTags.Player)
                    ?? FindAnyObjectByType<PlayGround.Player.PlayerRoot>();
            }

            if (mobSpawner == null)
            {
                mobSpawner = FindAnyObjectByType<MobSpawnerRoot>();
            }

            if (mobSpawner != null)
            {
                mobSpawner.BindCombatRoots(playerCombatRoot, mobCombatRoot);
            }

            if ((mobs == null || mobs.Length == 0) && mobSpawner == null)
            {
                mobs = FindTaggedComponents<MobRoot>(GameplayTags.Mob);
                if (mobs.Length == 0)
                {
                    throw new MissingReferenceException($"{nameof(GameRoot)} needs mobs or a {nameof(MobSpawnerRoot)}.");
                }
            }

            if (mobs != null)
            {
                for (int i = 0; i < mobs.Length; i++)
                {
                    if (mobs[i] == null)
                    {
                        throw new MissingReferenceException($"{nameof(GameRoot)} mob slot {i} is empty.");
                    }

                    if (playerCombatRoot.CanTarget(mobs[i]))
                    {
                        mobs[i].Register(playerCombatRoot.TargetRegistry);
                    }

                    // Player-faction root for the mob's status-triggered AOE (hits mobs).
                    mobs[i].BindAoeRoot(playerCombatRoot);

                    if (mobCombatRoot != null)
                    {
                        mobs[i].BindCombatRoot(mobCombatRoot);
                    }

                    if (player != null)
                    {
                        mobs[i].SetTarget(player.transform);
                    }
                }
            }

            if (mobCombatRoot != null && player != null && mobCombatRoot.CanTarget(player))
            {
                player.Register(mobCombatRoot.TargetRegistry);
            }

            if (gameplayCamera == null)
            {
                gameplayCamera = FindAnyObjectByType<GameplayCamera>();
            }

            if (playArea == null)
            {
                playArea = FindAnyObjectByType<PlayAreaRoot>();
            }

            if (gameplayCamera != null && player != null && playArea != null)
            {
                gameplayCamera.Configure(player.transform, playArea.Bounds);
            }
            else if (gameplayCamera != null && player != null)
            {
                gameplayCamera.Configure(player.transform);
            }
        }

        private void Start()
        {
            if (player != null && playerCombatRoot != null)
            {
                PlayGround.Skills.PlayerSkillDriver driver =
                    player.GetComponent<PlayGround.Skills.PlayerSkillDriver>();
                driver?.BindCombatRoot(playerCombatRoot);
            }
        }

        public void Configure(
            CombatRoot playerCombat,
            CombatRoot mobCombat,
            PlayGround.Player.PlayerRoot playerRoot,
            MobRoot[] mobRoots)
        {
            playerCombatRoot = playerCombat;
            mobCombatRoot = mobCombat;
            player = playerRoot;
            mobs = mobRoots;
        }

        public void Configure(
            CombatRoot playerCombat,
            CombatRoot mobCombat,
            PlayGround.Player.PlayerRoot playerRoot,
            MobSpawnerRoot spawner)
        {
            playerCombatRoot = playerCombat;
            mobCombatRoot = mobCombat;
            player = playerRoot;
            mobSpawner = spawner;
            mobs = null;
        }

        private static T FindTaggedComponent<T>(string tag)
            where T : Component
        {
            GameObject[] objects = FindTaggedObjects(tag);
            for (int i = 0; i < objects.Length; i++)
            {
                T component = objects[i].GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T[] FindTaggedComponents<T>(string tag)
            where T : Component
        {
            GameObject[] objects = FindTaggedObjects(tag);
            var components = new global::System.Collections.Generic.List<T>(objects.Length);
            for (int i = 0; i < objects.Length; i++)
            {
                T component = objects[i].GetComponent<T>();
                if (component != null)
                {
                    components.Add(component);
                }
            }

            return components.ToArray();
        }

        private static GameObject[] FindTaggedObjects(string tag)
        {
            try
            {
                return GameObject.FindGameObjectsWithTag(tag);
            }
            catch (UnityException)
            {
                return global::System.Array.Empty<GameObject>();
            }
        }
    }
}
