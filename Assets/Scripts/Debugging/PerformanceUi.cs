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
        private Label projectileTemplateRegistryText;
        private Label aoeTemplateRegistryText;
        private Label targetedTemplateRegistryText;
        private Label hitEventsText;
        private Label vfxEventsText;
        private Label soundEventsText;
        private Label vfxParticlesText;
        private VisualElement detailsGroup;
        private Button toggleButton;
        private bool detailsExpanded = true;
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
            projectileTemplateRegistryText = document.rootVisualElement.Q<Label>("performance-projectile-template-registry");
            aoeTemplateRegistryText = document.rootVisualElement.Q<Label>("performance-aoe-template-registry");
            targetedTemplateRegistryText = document.rootVisualElement.Q<Label>("performance-targeted-template-registry");
            hitEventsText = document.rootVisualElement.Q<Label>("performance-hit-events");
            vfxEventsText = document.rootVisualElement.Q<Label>("performance-vfx-events");
            soundEventsText = document.rootVisualElement.Q<Label>("performance-sound-events");
            vfxParticlesText = document.rootVisualElement.Q<Label>("performance-vfx-particles");
            detailsGroup = document.rootVisualElement.Q<VisualElement>("performance-details");
            toggleButton = document.rootVisualElement.Q<Button>("performance-toggle");
            if (performancePanel == null || fpsText == null || spawnReuseText == null || createEcbText == null
                || despawnedText == null || deleteEcbText == null || projectilesText == null || aoesText == null
                || targetedText == null || projectileTemplateRegistryText == null || aoeTemplateRegistryText == null
                || targetedTemplateRegistryText == null
                || hitEventsText == null || vfxEventsText == null || soundEventsText == null
                || vfxParticlesText == null
                || detailsGroup == null || toggleButton == null)
                throw new InvalidOperationException(
                    $"{nameof(PerformanceUi)} could not find its required elements. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            performancePanel.pickingMode = PickingMode.Ignore;
            toggleButton.clicked += ToggleDetails;
            ApplyDetailsVisibility();
        }

        private void OnDisable()
        {
            if (toggleButton != null)
                toggleButton.clicked -= ToggleDetails;

            ReleaseStatsQuery();
        }

        private void ToggleDetails()
        {
            detailsExpanded = !detailsExpanded;
            ApplyDetailsVisibility();
        }

        private void ApplyDetailsVisibility()
        {
            detailsGroup.style.display = detailsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            toggleButton.text = detailsExpanded ? "Show less" : "Show more";
        }

        private void Update()
        {
            smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            float frameTimeMilliseconds = smoothedDeltaTime * 1000f;
            fpsText.text = $"FPS:          {fps:0} ({frameTimeMilliseconds:0.0} ms)";

            // Collapsed rows are display:none, so the string formatting and the particle
            // walk behind them would be pure overhead in an overlay meant to measure cost.
            if (!detailsExpanded)
                return;

            CombatStatsDisplaySingleton stats = ReadCombatStats();
            spawnReuseText.text = $"Spawn reuse:  {stats.EntitiesSpawnedViaReuse}";
            createEcbText.text = $"Create ECB:   {stats.EntitiesSpawnedViaEcb}";
            despawnedText.text = $"Despawned:    {stats.EntitiesDespawned}";
            deleteEcbText.text = $"Delete ECB:   {stats.EntitiesDeleted}";
            projectilesText.text = $"Projectiles:  {stats.ActiveProjectiles}";
            aoesText.text = $"AOEs:         {stats.ActiveAoes}";
            targetedText.text = $"Targeted:     {stats.ActiveTargeted}";
            projectileTemplateRegistryText.text = $"Projectile templates: {stats.ProjectileTemplateRegistryEntries}";
            aoeTemplateRegistryText.text = $"AOE templates:        {stats.AoeTemplateRegistryEntries}";
            targetedTemplateRegistryText.text = $"Targeted templates:   {stats.TargetedTemplateRegistryEntries}";
            hitEventsText.text = $"Hit events:   {stats.HitEventsCreated}";
            vfxEventsText.text = $"VFX events:   {stats.VfxEventsCreated}";
            soundEventsText.text = $"Sound events: {stats.SoundEventsCreated}";
            vfxParticlesText.text = $"VFX particles: {CombatVfxRoot.AliveParticleCount(false)}";
        }

        private CombatStatsDisplaySingleton ReadCombatStats()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return default;

            if (!hasStatsQuery || statsWorld != world)
            {
                ReleaseStatsQuery();
                statsWorld = world;
                statsQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<CombatStatsDisplaySingleton>());
                hasStatsQuery = true;
            }

            return statsQuery.TryGetSingleton(out CombatStatsDisplaySingleton stats) ? stats : default;
        }

        // The query is owned by the world, so it dies with the world. This overlay has execution
        // order 1000, meaning its OnDisable runs after CombatRoot's teardown has already disposed
        // the world — disposing the handle then walks a freed query-data map and throws. When the
        // world is gone the query storage is already released, so dropping the handle is the whole
        // cleanup.
        private void ReleaseStatsQuery()
        {
            if (hasStatsQuery && statsWorld != null && statsWorld.IsCreated)
                statsQuery.Dispose();

            statsQuery = default;
            statsWorld = null;
            hasStatsQuery = false;
        }
    }
}
