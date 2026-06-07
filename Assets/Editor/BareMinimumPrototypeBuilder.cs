using System.IO;
using PlayGround.Audio;
using PlayGround.CameraSystem;
using PlayGround.Common;
using PlayGround.Game;
using PlayGround.Level;
using PlayGround.Mob;
using PlayGround.Mob.Behaviours;
using PlayGround.Mob.Triggers;
using PlayGround.Player;
using PlayGround.Spawn;
using PlayGround.System.Projectile;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PlayGround.Editor
{
    public static class BareMinimumPrototypeBuilder
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string PlayerSpritePath = "Assets/Shooter/Players/Tiles/tile_0000.png";
        private const string ProjectileSpritePath = "Assets/Shooter/Weapons/Tiles/tile_0000.png";
        private const string MobSpritePath = "Assets/Shooter/Enemies/Tiles/tile_0000.png";
        private const string GroundSpritePath = "Assets/Shooter/Tiles/Tiles/tile_0000.png";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const float OldPixelsPerUnit = 32f;

        [MenuItem("Tools/Play Ground/Build Bare Minimum Prototype")]
        public static void BuildBareMinimumPrototype()
        {
            EnsureFolders();
            EnsureLayers();
            ConfigureSprite(PlayerSpritePath, 32f);
            ConfigureSprite(ProjectileSpritePath, 32f);
            ConfigureSprite(MobSpritePath, 32f);
            ConfigureSprite(GroundSpritePath, 32f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Sprite playerSprite = LoadRequired<Sprite>(PlayerSpritePath);
            Sprite projectileSprite = LoadRequired<Sprite>(ProjectileSpritePath);
            Sprite mobSprite = LoadRequired<Sprite>(MobSpritePath);
            Sprite groundSprite = LoadRequired<Sprite>(GroundSpritePath);
            InputActionAsset inputActions = LoadRequired<InputActionAsset>(InputActionsPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Main";

            GameObject gameRootObject = new("GameRoot");
            GameRoot gameRoot = gameRootObject.AddComponent<GameRoot>();

            GameObject audioObject = new("AudioManager");
            audioObject.AddComponent<AudioManager>();

            GameObject projectileObject = new("ProjectileRoot_PlayerToMob");
            ProjectileRoot projectileRoot = projectileObject.AddComponent<ProjectileRoot>();
            projectileRoot.Configure(projectileSprite);

            GameObject mobProjectileObject = new("ProjectileRoot_MobToPlayer");
            ProjectileRoot mobProjectileRoot = mobProjectileObject.AddComponent<ProjectileRoot>();
            mobProjectileRoot.Configure(projectileSprite);

            GameObject player = CreatePlayer(playerSprite, inputActions);
            MobRoot[] mobPrefabs = CreateMobPrefabAssets(mobSprite);
            MobSpawnPool spawnPool = EnsureSpawnPoolAsset("Assets/ScriptableObjects/Spawn/StarterMobPool.asset", mobPrefabs);
            GameObject spawnerObject = CreateSpawner(spawnPool, player.transform, projectileRoot, mobProjectileRoot);
            MobSpawnerRoot spawner = spawnerObject.GetComponent<MobSpawnerRoot>();
            GameObject cameraObject = CreateCamera(player.transform);
            Camera worldCamera = cameraObject.GetComponent<Camera>();
            player.GetComponent<PlayerRoot>().Configure(
                inputActions,
                player.GetComponent<Rigidbody2D>(),
                player.transform.Find("Hurtbox").GetComponent<Collider2D>(),
                player.GetComponentInChildren<SpriteRenderer>(),
                worldCamera);

            gameRoot.Configure(projectileRoot, mobProjectileRoot, player.GetComponent<PlayerRoot>(), spawner);

            GameObject level = CreateLevel(groundSprite);
            level.GetComponent<PlayAreaRoot>().BuildRuntimeWalls();

            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player/Player.prefab");
            PrefabUtility.SaveAsPrefabAsset(projectileObject, "Assets/Prefabs/Projectiles/ProjectileRoot_PlayerToMob.prefab");
            PrefabUtility.SaveAsPrefabAsset(mobProjectileObject, "Assets/Prefabs/Projectiles/ProjectileRoot_MobToPlayer.prefab");

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static GameObject CreatePlayer(Sprite sprite, InputActionAsset inputActions)
        {
            GameObject player = new("PlayerRoot");
            player.layer = GameplayLayers.RequiredLayer(GameplayLayers.PlayerBody);
            player.transform.position = new Vector3(-3f, 0f, 0f);

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D bodyCollider = player.AddComponent<CircleCollider2D>();
            bodyCollider.radius = 0.45f;

            GameObject visual = new("Visual");
            visual.transform.SetParent(player.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 10;

            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.layer = GameplayLayers.RequiredLayer(GameplayLayers.PlayerHurtbox);
            hurtboxObject.transform.SetParent(player.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.45f;
            hurtbox.isTrigger = true;

            PlayerRoot playerRoot = player.AddComponent<PlayerRoot>();
            playerRoot.Configure(inputActions, body, hurtbox, renderer, null);

            return player;
        }

        private static MobRoot[] CreateMobPrefabAssets(Sprite sprite)
        {
            MobBehaviour wander = EnsureMobAsset<WanderBehaviour>("Assets/ScriptableObjects/Mobs/Wander.asset");
            MobBehaviour swarm = EnsureMobAsset<SwarmTargetBehaviour>("Assets/ScriptableObjects/Mobs/SwarmTarget.asset");
            MobTrigger sensor = EnsureMobAsset<TargetSensorTrigger>("Assets/ScriptableObjects/Mobs/TargetSensor.asset");
            MobTrigger recovery = EnsureMobAsset<HurtRecoveryTrigger>("Assets/ScriptableObjects/Mobs/HurtRecovery.asset");
            var mappings = new[]
            {
                new MobTriggerBehaviourMapping { triggerKey = MobRoot.DefaultTriggerKey, behaviourKey = "wander" },
                new MobTriggerBehaviourMapping { triggerKey = "on_target_seen", behaviourKey = "swarm_target" }
            };

            GameObject slime = CreateMob<SlimeRoot>("Slime", sprite, Vector3.zero, null, WorldUnits(30f), 35f, 0.5f, null, wander, swarm, sensor, recovery, mappings);
            GameObject skeleton = CreateMob<SkeletonRoot>("Skeleton", sprite, Vector3.zero, null, WorldUnits(45f), 30f, 0.45f, null, wander, swarm, sensor, recovery, mappings);
            GameObject bat = CreateMob<BatRoot>("Bat", sprite, Vector3.zero, null, WorldUnits(60f), 20f, 0.35f, null, wander, swarm, sensor, recovery, mappings);
            bat.GetComponent<BatRoot>().ConfigureProjectileAttack(null, 1.4f, WorldUnits(130f), WorldUnits(12f), WorldUnits(220f), 1.8f, 1f, 0.25f);

            GameObject slimePrefab = PrefabUtility.SaveAsPrefabAsset(slime, "Assets/Prefabs/Mobs/Slime.prefab");
            GameObject skeletonPrefab = PrefabUtility.SaveAsPrefabAsset(skeleton, "Assets/Prefabs/Mobs/Skeleton.prefab");
            GameObject batPrefab = PrefabUtility.SaveAsPrefabAsset(bat, "Assets/Prefabs/Mobs/Bat.prefab");

            Object.DestroyImmediate(slime);
            Object.DestroyImmediate(skeleton);
            Object.DestroyImmediate(bat);

            return new[]
            {
                slimePrefab.GetComponent<MobRoot>(),
                skeletonPrefab.GetComponent<MobRoot>(),
                batPrefab.GetComponent<MobRoot>()
            };
        }

        private static GameObject CreateMob<T>(
            string name,
            Sprite sprite,
            Vector3 position,
            Transform target,
            float speed,
            float health,
            float radius,
            ProjectileRoot mobProjectileRoot,
            MobBehaviour wander,
            MobBehaviour swarm,
            MobTrigger sensor,
            MobTrigger recovery,
            MobTriggerBehaviourMapping[] mappings)
            where T : MobRoot
        {
            GameObject mob = new(name);
            mob.layer = GameplayLayers.RequiredLayer(GameplayLayers.MobBody);
            mob.transform.position = position;

            Rigidbody2D body = mob.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;

            CircleCollider2D bodyCollider = mob.AddComponent<CircleCollider2D>();
            bodyCollider.radius = radius;

            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.layer = GameplayLayers.RequiredLayer(GameplayLayers.MobHurtbox);
            hurtboxObject.transform.SetParent(mob.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = radius;
            hurtbox.isTrigger = true;

            GameObject visual = new("Visual");
            visual.transform.SetParent(mob.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 9;

            T root = mob.AddComponent<T>();
            root.Configure(body, bodyCollider, hurtbox, renderer, target);
            root.ConfigureAuthoring(
                new[] { wander, swarm },
                new[] { sensor, recovery },
                mappings,
                health,
                speed,
                radius);
            if (mobProjectileRoot != null)
            {
                root.ConfigureProjectileAttack(mobProjectileRoot, 1.4f, 130f, 12f, 220f, 1.8f, 1f, 0.25f);
            }

            return mob;
        }

        private static GameObject CreateSpawner(
            MobSpawnPool pool,
            Transform target,
            ProjectileRoot playerProjectileRoot,
            ProjectileRoot mobProjectileRoot)
        {
            GameObject spawnerObject = new("MobSpawnerRoot");
            MobSpawnerRoot spawner = spawnerObject.AddComponent<MobSpawnerRoot>();
            SpawnPoint[] points =
            {
                CreateSpawnPoint(spawnerObject.transform, pool, "SpawnPoint_SouthEast", new Vector3(5f, -2f, 0f), 1.0f),
                CreateSpawnPoint(spawnerObject.transform, pool, "SpawnPoint_East", new Vector3(6f, 0f, 0f), 1.4f),
                CreateSpawnPoint(spawnerObject.transform, pool, "SpawnPoint_NorthEast", new Vector3(5f, 2f, 0f), 1.8f)
            };
            spawner.Configure(pool, 20, target, playerProjectileRoot, mobProjectileRoot, points);
            return spawnerObject;
        }

        private static SpawnPoint CreateSpawnPoint(Transform parent, MobSpawnPool pool, string name, Vector3 position, float interval)
        {
            GameObject pointObject = new(name);
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = position;
            SpawnPoint point = pointObject.AddComponent<SpawnPoint>();
            point.Configure(pool, interval, 1.25f, 4);
            return point;
        }

        private static GameObject CreateCamera(Transform target)
        {
            GameObject cameraObject = new("GameplayCamera");
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.06f);
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<GameplayCamera>().Configure(target, new Rect(-12f, -7f, 24f, 14f));
            return cameraObject;
        }

        private static GameObject CreateLevel(Sprite groundSprite)
        {
            GameObject level = new("PlayArea");
            PlayAreaRoot root = level.AddComponent<PlayAreaRoot>();
            root.Configure(new Vector2(24f, 14f), 0.5f);

            GameObject ground = new("GroundDebugTiles");
            ground.transform.SetParent(level.transform, false);
            for (int y = -7; y <= 7; y++)
            {
                for (int x = -12; x <= 12; x++)
                {
                    GameObject tile = new($"Ground_{x}_{y}");
                    tile.transform.SetParent(ground.transform, false);
                    tile.transform.localPosition = new Vector3(x, y, 1f);
                    SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                    renderer.sprite = groundSprite;
                    renderer.color = new Color(0.18f, 0.20f, 0.19f, 1f);
                    renderer.sortingOrder = -10;
                }
            }

            return level;
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required asset missing: {path}");
            }

            return asset;
        }

        private static void ConfigureSprite(string path, float pixelsPerUnit)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/Scenes",
                "Assets/Prefabs",
                "Assets/Prefabs/Player",
                "Assets/Prefabs/Mobs",
                "Assets/Prefabs/Projectiles",
                "Assets/ScriptableObjects",
                "Assets/ScriptableObjects/Mobs",
                "Assets/ScriptableObjects/Spawn"
            };

            foreach (string folder in folders)
            {
                Directory.CreateDirectory(folder);
            }
        }

        private static void EnsureLayers()
        {
            string[] layers =
            {
                GameplayLayers.PlayerBody,
                GameplayLayers.PlayerHurtbox,
                "PlayerProjectile",
                "PlayerAoe",
                GameplayLayers.MobBody,
                GameplayLayers.MobHurtbox,
                "MobProjectile",
                "MobAoe",
                GameplayLayers.Environment
            };

            SerializedObject tagManager = new(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layerProperty = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.Length; i++)
            {
                SerializedProperty slot = layerProperty.GetArrayElementAtIndex(8 + i);
                slot.stringValue = layers[i];
            }

            tagManager.ApplyModifiedProperties();
        }

        private static T EnsureMobAsset<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static MobSpawnPool EnsureSpawnPoolAsset(string path, MobRoot[] prefabs)
        {
            MobSpawnPool pool = AssetDatabase.LoadAssetAtPath<MobSpawnPool>(path);
            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<MobSpawnPool>();
                AssetDatabase.CreateAsset(pool, path);
            }

            pool.Configure(prefabs);
            EditorUtility.SetDirty(pool);
            return pool;
        }

        private static float WorldUnits(float oldPixels)
        {
            return oldPixels / OldPixelsPerUnit;
        }
    }
}
