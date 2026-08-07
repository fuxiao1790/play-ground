using System;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.SkillUi
{
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PerformanceUi : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement performancePanel;
        private Label fpsText;
        private Label spawnReuseText;
        private Label createEcbText;
        private Label despawnedText;
        private Label deleteEcbText;
        private Label projectilesText;
        private Label aoesText;
        private Label targetedText;
        private Label hitEventsText;
        private Label vfxEventsText;
        private Label vfxParticlesText;
        private float smoothedDeltaTime;
        private World statsWorld;
        private EntityQuery statsQuery;
        private bool hasStatsQuery;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            if (document == null)
                throw new InvalidOperationException($"{nameof(PerformanceUi)} requires a {nameof(UIDocument)} component.");
        }

        private void OnEnable()
        {
            performancePanel = document.rootVisualElement.Q<VisualElement>("performance-panel");
            fpsText = document.rootVisualElement.Q<Label>("performance-fps");
            spawnReuseText = document.rootVisualElement.Q<Label>("performance-spawn-reuse");
            createEcbText = document.rootVisualElement.Q<Label>("performance-create-ecb");
            despawnedText = document.rootVisualElement.Q<Label>("performance-despawned");
            deleteEcbText = document.rootVisualElement.Q<Label>("performance-delete-ecb");
            projectilesText = document.rootVisualElement.Q<Label>("performance-projectiles");
            aoesText = document.rootVisualElement.Q<Label>("performance-aoes");
            targetedText = document.rootVisualElement.Q<Label>("performance-targeted");
            hitEventsText = document.rootVisualElement.Q<Label>("performance-hit-events");
            vfxEventsText = document.rootVisualElement.Q<Label>("performance-vfx-events");
            vfxParticlesText = document.rootVisualElement.Q<Label>("performance-vfx-particles");
            if (performancePanel == null || fpsText == null || spawnReuseText == null || createEcbText == null
                || despawnedText == null || deleteEcbText == null || projectilesText == null || aoesText == null
                || targetedText == null
                || hitEventsText == null || vfxEventsText == null || vfxParticlesText == null)
                throw new InvalidOperationException(
                    $"{nameof(PerformanceUi)} could not find its required elements. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            performancePanel.pickingMode = PickingMode.Ignore;
        }

        private void OnDisable()
        {
            if (hasStatsQuery)
                statsQuery.Dispose();

            statsWorld = null;
            hasStatsQuery = false;
        }

        private void Update()
        {
            smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            CombatStatsDisplaySingleton stats = ReadCombatStats();

            fpsText.text = $"FPS:          {fps:0}";
            spawnReuseText.text = $"Spawn reuse:  {stats.EntitiesSpawnedViaReuse}";
            createEcbText.text = $"Create ECB:   {stats.EntitiesSpawnedViaEcb}";
            despawnedText.text = $"Despawned:    {stats.EntitiesDespawned}";
            deleteEcbText.text = $"Delete ECB:   {stats.EntitiesDeleted}";
            projectilesText.text = $"Projectiles:  {stats.ActiveProjectiles}";
            aoesText.text = $"AOEs:         {stats.ActiveAoes}";
            targetedText.text = $"Targeted:     {stats.ActiveTargeted}";
            hitEventsText.text = $"Hit events:   {stats.HitEventsCreated}";
            vfxEventsText.text = $"VFX events:   {stats.VfxEventsCreated}";
            vfxParticlesText.text = $"VFX particles: {CombatVfxRoot.AliveParticleCount(false)}";
        }

        private CombatStatsDisplaySingleton ReadCombatStats()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return default;

            if (!hasStatsQuery || statsWorld != world)
            {
                if (hasStatsQuery)
                    statsQuery.Dispose();

                statsWorld = world;
                statsQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<CombatStatsDisplaySingleton>());
                hasStatsQuery = true;
            }

            return statsQuery.TryGetSingleton(out CombatStatsDisplaySingleton stats) ? stats : default;
        }
    }
}
