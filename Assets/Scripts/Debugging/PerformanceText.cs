using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class PerformanceText : MonoBehaviour
{
    [SerializeField] private Text text;
    [SerializeField] private Vector2 padding = new(12f, 12f);
    [SerializeField] private Vector2 size = new(320f, 72f);
    [SerializeField] private int fontSize = 18;

    private EntityQuery projectileQuery;
    private float smoothedDeltaTime;
    private bool queryReady;

    private void Awake()
    {
        EnsureOverlayText();
    }

    private void Start()
    {
        TryBindProjectileQuery();
    }

    private void Update()
    {
        if (text == null)
        {
            return;
        }

        if (!queryReady)
        {
            TryBindProjectileQuery();
        }

        smoothedDeltaTime += (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;

        float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;

        int entityCount = queryReady ? projectileQuery.CalculateEntityCount() : 0;

        text.text =
            $"FPS: {fps:0}\n" +
            $"Projectiles: {entityCount:n0}";
    }

    private void TryBindProjectileQuery()
    {
        if (World.DefaultGameObjectInjectionWorld == null
            || !World.DefaultGameObjectInjectionWorld.IsCreated)
        {
            return;
        }

        EntityManager entityManager =
            World.DefaultGameObjectInjectionWorld.EntityManager;

        projectileQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayGround.System.Projectile.ProjectileComponent>(),
            ComponentType.ReadOnly<PlayGround.System.Projectile.ProjectileActiveTag>());
        queryReady = true;
    }

    private void EnsureOverlayText()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        if (text == null)
        {
            text = GetComponentInChildren<Text>(true);
        }

        if (text == null)
        {
            GameObject labelObject = new("PerformanceLabel");
            labelObject.transform.SetParent(transform, false);
            labelObject.AddComponent<CanvasRenderer>();
            text = labelObject.AddComponent<Text>();
        }

        RectTransform rectTransform = text.rectTransform;
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
