using PlayGround.Attack;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.Spawn;
using PlayGround.System.Aoe;
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
        [SerializeField] private MobSpawnerRoot mobSpawner;
        [SerializeField] private MobRoot[] mobs;
        [SerializeField] private PlayGround.Player.PlayerRoot player;

        private readonly CombatSpawnRouter combatSpawnRouter = new();

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

            if (mobSpawner != null && playerAoeRoot != null)
            {
                mobSpawner.BindAoeRoot(playerAoeRoot);
            }

            if (mobSpawner != null)
            {
                mobSpawner.BindProjectileRoots(playerProjectileRoot, mobProjectileRoot);
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

                    if (playerProjectileRoot.CanTarget(mobs[i]))
                    {
                        mobs[i].Register(playerProjectileRoot.TargetRegistry);
                    }

                    if (playerAoeRoot != null)
                    {
                        mobs[i].BindAoeRoot(playerAoeRoot);
                        mobs[i].Register(playerAoeRoot.TargetRegistry);
                    }

                    if (player != null)
                    {
                        mobs[i].SetTarget(player.transform);
                    }
                }
            }

            if (mobProjectileRoot != null && player != null)
            {
                if (mobProjectileRoot.CanTarget(player))
                {
                    player.Register(mobProjectileRoot.TargetRegistry);
                }
            }

            if (mobAoeRoot != null && player != null)
            {
                player.Register(mobAoeRoot.TargetRegistry);
            }

            combatSpawnRouter.Bind(playerProjectileRoot, mobProjectileRoot, playerAoeRoot, mobAoeRoot);
        }

        private void Start()
        {
            if (player != null && playerAoeRoot != null)
            {
                BindPlayerAoeAttacks(playerAoeRoot);
            }
        }

        private void OnDestroy()
        {
            combatSpawnRouter.Unbind();
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

        private void BindPlayerAoeAttacks(AoeRoot root)
        {
            ProjectileAttack[] projectileAttacks = player.GetComponentsInChildren<ProjectileAttack>(true);
            for (int i = 0; i < projectileAttacks.Length; i++)
            {
                projectileAttacks[i].ConfigureAoeRoot(root);
            }

            AoeAttack[] aoeAttacks = player.GetComponentsInChildren<AoeAttack>(true);
            for (int i = 0; i < aoeAttacks.Length; i++)
            {
                aoeAttacks[i].Configure(root);
            }

            ChildSpawningProjectileAttack[] childSpawningAttacks = player.GetComponentsInChildren<ChildSpawningProjectileAttack>(true);
            for (int i = 0; i < childSpawningAttacks.Length; i++)
            {
                childSpawningAttacks[i].ConfigureAoeRoot(root);
            }
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
