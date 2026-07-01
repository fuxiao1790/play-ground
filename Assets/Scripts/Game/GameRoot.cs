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
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private MobSpawnerRoot mobSpawner;
        [SerializeField] private MobRoot[] mobs;
        [SerializeField] private PlayGround.Player.PlayerRoot player;
        [SerializeField] private GameplayCamera gameplayCamera;
        [SerializeField] private PlayAreaRoot playArea;

        private void Awake()
        {
            ConfigureFramePacing();

            if (combatRoot == null)
            {
                combatRoot = FindTaggedComponent<CombatRoot>(GameplayTags.PlayerProjectileRoot);
            }

            if (combatRoot == null)
            {
                throw new MissingReferenceException($"{nameof(GameRoot)} needs a combat root.");
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
                mobSpawner.BindCombatRoot(combatRoot);
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

                    mobs[i].Register(combatRoot.TargetRegistry);
                    mobs[i].BindCombatRoot(combatRoot);

                    if (player != null)
                    {
                        mobs[i].SetTarget(player.transform);
                    }
                }
            }

            if (player != null)
            {
                player.Register(combatRoot.TargetRegistry);
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
            if (player != null && combatRoot != null)
            {
                PlayGround.Skills.PlayerSkillDriver driver =
                    player.GetComponent<PlayGround.Skills.PlayerSkillDriver>();
                driver?.BindCombatRoot(combatRoot);
            }
        }

        private static void ConfigureFramePacing()
        {
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        public void Configure(
            CombatRoot combat,
            PlayGround.Player.PlayerRoot playerRoot,
            MobRoot[] mobRoots)
        {
            combatRoot = combat;
            player = playerRoot;
            mobs = mobRoots;
        }

        public void Configure(
            CombatRoot combat,
            PlayGround.Player.PlayerRoot playerRoot,
            MobSpawnerRoot spawner)
        {
            combatRoot = combat;
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
