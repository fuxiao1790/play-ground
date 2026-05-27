using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class PerformanceText : MonoBehaviour
{
    [SerializeField] private Text text;

    private EntityQuery projectileQuery;
    private float smoothedDeltaTime;

    private void Start()
    {
        EntityManager entityManager =
            World.DefaultGameObjectInjectionWorld.EntityManager;

        projectileQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ProjectileComponent>(),
            ComponentType.ReadOnly<ProjectileActiveTag>());
    }

    private void Update()
    {
        smoothedDeltaTime +=
            (Time.unscaledDeltaTime - smoothedDeltaTime) * 0.1f;

        float fps = 1f / smoothedDeltaTime;

        int entityCount = projectileQuery.CalculateEntityCount();

        text.text =
            $"FPS: {fps:0}\n" +
            $"Projectiles: {entityCount:n0}";
    }
}