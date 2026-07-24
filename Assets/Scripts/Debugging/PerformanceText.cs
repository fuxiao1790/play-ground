using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class PerformanceText : MonoBehaviour
{
    [SerializeField] private Text text;
    [SerializeField] private Vector2 padding = new(12f, 12f);
    [SerializeField] private Vector2 size = new(360f, 180f);
    [SerializeField] private int fontSize = 18;

    private float smoothedDeltaTime;

    private void Awake()
    {
        EnsureOverlayText();
    }

    private void Update()
    {
        if (text == null)
        {
            return;
        }

        smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
        float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;

        // Pull the shared stats straight from the ECS world instead of the simulation
        // pushing them here — keeps the sim assembly free of any Debugging reference.
        CombatStatsSingleton stats = ReadCombatStats();
        text.text =
            $"FPS:          {fps:0}\n" +
            $"Spawn reuse:  {stats.EntitiesSpawnedViaReuse}\n" +
            $"Create ECB:   {stats.EntitiesSpawnedViaEcb}\n" +
            $"Despawned:    {stats.EntitiesDespawned}\n" +
            $"Delete ECB:   {stats.EntitiesDeleted}\n" +
            $"Projectiles:  {stats.ActiveProjectiles}\n" +
            $"AOEs:         {stats.ActiveAoes}\n" +
            $"Hit events:   {stats.HitEventsCreated}\n" +
            $"VFX events:   {stats.VfxEventsCreated}\n" +
            $"VFX particles: {CombatVfxRoot.AliveParticleCount(false)}\n";
    }

    // Read-only pull of the simulation's per-frame stats singleton. The gather system
    // publishes it to the ECS world each frame; the overlay queries the world for it here.
    private static CombatStatsSingleton ReadCombatStats()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return default;
        }

        EntityQuery query = world.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<CombatStatsSingleton>());
        return query.TryGetSingleton(out CombatStatsSingleton stats) ? stats : default;
    }

    private void EnsureOverlayText()
    {
        Canvas canvas = GetComponent<Canvas>();
        GameObject canvasGO;

        if (canvas == null)
        {
            canvasGO = new GameObject("PerformanceOverlayCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            canvasGO.transform.SetParent(null);
        }
        else
        {
            canvasGO = canvas.gameObject;
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasGO.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        if (text == null)
        {
            text = canvasGO.GetComponentInChildren<Text>(true) ?? GetComponentInChildren<Text>(true);
        }

        if (text == null)
        {
            GameObject labelObject = new GameObject("PerformanceLabel");
            labelObject.transform.SetParent(canvasGO.transform, false);
            labelObject.AddComponent<CanvasRenderer>();
            text = labelObject.AddComponent<Text>();
        }

        RectTransform rectTransform = text.rectTransform;
        rectTransform.SetParent(canvasGO.transform, false);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(padding.x, -padding.y);
        rectTransform.sizeDelta = size;

        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.UpperLeft;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }
}
