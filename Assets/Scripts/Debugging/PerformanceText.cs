using PlayGround.System.Stats;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class PerformanceText : MonoBehaviour
{
    [SerializeField] private Text text;
    [SerializeField] private Vector2 padding = new(12f, 12f);
    [SerializeField] private Vector2 size = new(360f, 156f);
    [SerializeField] private int fontSize = 18;

    private CombatStatsGatherSystem statsSystem;
    private CombatStatsSingleton stats;
    private bool boundToStats;
    private float smoothedDeltaTime;

    private void Awake()
    {
        EnsureOverlayText();
    }

    private void OnEnable()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        statsSystem = world?.GetExistingSystemManaged<CombatStatsGatherSystem>();
        if (statsSystem == null)
        {
            Debug.LogWarning(
                "[PerformanceText] CombatStatsGatherSystem not found; ECS stats will not display.");
            return;
        }

        statsSystem.Bind(this);
        boundToStats = true;
    }

    private void OnDisable()
    {
        if (boundToStats)
        {
            statsSystem?.Unbind(this);
        }

        boundToStats = false;
        statsSystem = null;
    }

    internal void Apply(in CombatStatsSingleton snapshot) => stats = snapshot;

    private void Update()
    {
        if (text == null)
        {
            return;
        }

        smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;
        float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
        text.text =
            $"FPS: {fps:0}\n" +
            $"Spawn ECB:   {stats.EntitiesSpawnedViaEcb}\n" +
            $"Spawn reuse: {stats.EntitiesSpawnedViaReuse}\n" +
            $"Projectiles:  {stats.ActiveProjectiles}\n" +
            $"AOEs:         {stats.ActiveAoes}\n" +
            $"Hit events:  {stats.HitEventsCreated}\n" +
            $"VFX events:  {stats.VfxEventsCreated}";
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
