using PlayGround.CameraSystem;
using PlayGround.Common;
using PlayGround.Level;
using PlayGround.Mob;
using PlayGround.Spawn;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Game
{
    public sealed class GameRoot : MonoBehaviour
    {
        [SerializeField] private ProjectileRoot playerProjectileRoot;
        [SerializeField] private ProjectileRoot mobProjectileRoot;
        [SerializeField] private AoeRoot playerAoeRoot;
        [SerializeField] private AoeRoot mobAoeRoot;
        [SerializeField] private CombatRuntimeRoot combatRuntimeRoot;
        [SerializeField] private MobSpawnerRoot mobSpawner;
        [SerializeField] private MobRoot[] mobs;
        [SerializeField] private PlayGround.Player.PlayerRoot player;
        [SerializeField] private GameplayCamera gameplayCamera;
        [SerializeField] private PlayAreaRoot playArea;

        private readonly CombatSpawnRouter combatSpawnRouter = new();
        private const string PrimaryTargetSetKey = "PrimaryTargets";
        private const string SecondaryTargetSetKey = "SecondaryTargets";

        private void Awake()
        {
            if (playerProjectileRoot == null)
            {
                playerProjectileRoot = FindTaggedComponent<ProjectileRoot>(GameplayTags.PlayerProjectileRoot);
            }

            if (playerProjectileRoot == null)
            {
                throw new MissingReferenceException($"{nameof(GameRoot)} needs a player projectile root.");
            }

            if (mobProjectileRoot == null)
            {
                mobProjectileRoot = FindTaggedComponent<ProjectileRoot>(GameplayTags.MobProjectileRoot);
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

            EnsureCombatRuntimeRoot();
            BindCombatScopes();

            if (mobSpawner != null && playerAoeRoot != null)
            {
                mobSpawner.BindAoeRoot(playerAoeRoot);
            }

            if (mobSpawner != null)
            {
                mobSpawner.BindProjectileRoots(playerProjectileRoot, mobProjectileRoot);
                mobSpawner.BindCombatRuntime(combatRuntimeRoot, PrimaryTargetSetKey);
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

                    if (combatRuntimeRoot != null)
                    {
                        mobs[i].Register(combatRuntimeRoot.GetOrCreateTargetSet(PrimaryTargetSetKey));
                    }
                    else if (playerProjectileRoot.CanTarget(mobs[i]))
                    {
                        mobs[i].Register(playerProjectileRoot.TargetRegistry);
                    }

                    if (playerAoeRoot != null)
                    {
                        mobs[i].BindAoeRoot(playerAoeRoot);
                        if (combatRuntimeRoot == null)
                        {
                            mobs[i].Register(playerAoeRoot.TargetRegistry);
                        }
                    }

                    if (player != null)
                    {
                        mobs[i].SetTarget(player.transform);
                    }
                }
            }

            if (combatRuntimeRoot != null && player != null)
            {
                player.Register(combatRuntimeRoot.GetOrCreateTargetSet(SecondaryTargetSetKey));
            }
            else if (mobProjectileRoot != null && player != null)
            {
                if (mobProjectileRoot.CanTarget(player))
                {
                    player.Register(mobProjectileRoot.TargetRegistry);
                }
            }

            if (mobAoeRoot != null && player != null && combatRuntimeRoot == null)
            {
                player.Register(mobAoeRoot.TargetRegistry);
            }

            combatSpawnRouter.Bind(playerProjectileRoot, mobProjectileRoot, playerAoeRoot, mobAoeRoot);

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
            if (player != null && playerAoeRoot != null)
            {
                PlayGround.Skills.PlayerSkillDriver driver =
                    player.GetComponent<PlayGround.Skills.PlayerSkillDriver>();
                driver?.BindAoeRoot(playerAoeRoot);
            }
        }

        private void OnDestroy()
        {
            combatSpawnRouter.Unbind();
        }

        private void EnsureCombatRuntimeRoot()
        {
            if (combatRuntimeRoot != null)
            {
                return;
            }

            combatRuntimeRoot = GetComponent<CombatRuntimeRoot>();
            if (combatRuntimeRoot == null)
            {
                combatRuntimeRoot = FindAnyObjectByType<CombatRuntimeRoot>();
            }

            if (combatRuntimeRoot == null)
            {
                combatRuntimeRoot = gameObject.AddComponent<CombatRuntimeRoot>();
            }
        }

        private void BindCombatScopes()
        {
            if (combatRuntimeRoot == null)
            {
                return;
            }

            if (playerProjectileRoot != null)
            {
                combatRuntimeRoot.BindScope(playerProjectileRoot, PrimaryTargetSetKey);
            }

            if (playerAoeRoot != null)
            {
                combatRuntimeRoot.BindScope(playerAoeRoot, PrimaryTargetSetKey);
            }

            if (mobProjectileRoot != null)
            {
                combatRuntimeRoot.BindScope(mobProjectileRoot, SecondaryTargetSetKey);
            }

            if (mobAoeRoot != null)
            {
                combatRuntimeRoot.BindScope(mobAoeRoot, SecondaryTargetSetKey);
            }
        }

        public void Configure(ProjectileRoot projectileRoot, MobRoot[] mobRoots)
        {
            playerProjectileRoot = projectileRoot;
            mobs = mobRoots;
        }

        public void Configure(ProjectileRoot projectileRoot, ProjectileRoot mobToPlayerProjectileRoot, PlayGround.Player.PlayerRoot playerRoot, MobRoot[] mobRoots)
        {
            playerProjectileRoot = projectileRoot;
            mobProjectileRoot = mobToPlayerProjectileRoot;
            player = playerRoot;
            mobs = mobRoots;
        }

        public void Configure(
            ProjectileRoot projectileRoot,
            ProjectileRoot mobToPlayerProjectileRoot,
            AoeRoot playerToMobAoeRoot,
            AoeRoot mobToPlayerAoeRoot,
            PlayGround.Player.PlayerRoot playerRoot,
            MobRoot[] mobRoots)
        {
            playerProjectileRoot = projectileRoot;
            mobProjectileRoot = mobToPlayerProjectileRoot;
            playerAoeRoot = playerToMobAoeRoot;
            mobAoeRoot = mobToPlayerAoeRoot;
            player = playerRoot;
            mobs = mobRoots;
        }

        public void Configure(ProjectileRoot projectileRoot, ProjectileRoot mobToPlayerProjectileRoot, PlayGround.Player.PlayerRoot playerRoot, MobSpawnerRoot spawner)
        {
            playerProjectileRoot = projectileRoot;
            mobProjectileRoot = mobToPlayerProjectileRoot;
            player = playerRoot;
            mobSpawner = spawner;
            mobs = null;
        }

        public void Configure(
            ProjectileRoot projectileRoot,
            ProjectileRoot mobToPlayerProjectileRoot,
            AoeRoot playerToMobAoeRoot,
            AoeRoot mobToPlayerAoeRoot,
            PlayGround.Player.PlayerRoot playerRoot,
            MobSpawnerRoot spawner)
        {
            playerProjectileRoot = projectileRoot;
            mobProjectileRoot = mobToPlayerProjectileRoot;
            playerAoeRoot = playerToMobAoeRoot;
            mobAoeRoot = mobToPlayerAoeRoot;
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
