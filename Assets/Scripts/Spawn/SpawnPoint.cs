using System;
using UnityEngine;

namespace PlayGround.Spawn
{
    public sealed class SpawnPoint : MonoBehaviour
    {
        private const int MaxPolygonSampleAttempts = 128;

        [SerializeField] private PolygonCollider2D areaShape;

        private Vector2[][] polygonPaths = Array.Empty<Vector2[]>();
        private Rect polygonLocalBounds;
        private bool hasPolygonArea;

        private void Awake()
        {
            ResolveAreaShape();
            RefreshAreaCache();
        }

        private void OnValidate()
        {
            ResolveAreaShape();
            RefreshAreaCache();
        }

        public Vector2 SamplePosition(global::System.Random rng)
        {
            rng ??= new global::System.Random();
            if (TrySamplePolygon(rng, out Vector2 polygonPosition))
            {
                return polygonPosition;
            }

            float x = (float)rng.NextDouble() - 0.5f;
            float y = (float)rng.NextDouble() - 0.5f;
            return transform.TransformPoint(new Vector3(x, y, 0f));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            PolygonCollider2D shape = areaShape != null ? areaShape : GetComponent<PolygonCollider2D>();
            if (shape != null && shape.pathCount > 0)
            {
                DrawPolygonGizmo(shape);
                return;
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 1f, 0f));
            Gizmos.matrix = previousMatrix;
        }

        private void RefreshAreaCache()
        {
            polygonPaths = Array.Empty<Vector2[]>();
            polygonLocalBounds = default;
            hasPolygonArea = false;

            if (areaShape == null || areaShape.pathCount <= 0)
            {
                return;
            }

            Vector2 offset = areaShape.offset;
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            Vector2[][] paths = new Vector2[areaShape.pathCount][];
            bool hasAnyPath = false;

            for (int pathIndex = 0; pathIndex < areaShape.pathCount; pathIndex++)
            {
                Vector2[] sourcePath = areaShape.GetPath(pathIndex);
                if (sourcePath == null || sourcePath.Length < 3)
                {
                    paths[pathIndex] = Array.Empty<Vector2>();
                    continue;
                }

                Vector2[] cachedPath = new Vector2[sourcePath.Length];
                for (int pointIndex = 0; pointIndex < sourcePath.Length; pointIndex++)
                {
                    Vector2 point = sourcePath[pointIndex] + offset;
                    cachedPath[pointIndex] = point;
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                paths[pathIndex] = cachedPath;
                hasAnyPath = true;
            }

            if (!hasAnyPath)
            {
                return;
            }

            polygonPaths = paths;
            polygonLocalBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            hasPolygonArea = polygonLocalBounds.width > 0f && polygonLocalBounds.height > 0f;
        }

        private bool TrySamplePolygon(global::System.Random rng, out Vector2 position)
        {
            position = default;
            if (!hasPolygonArea || areaShape == null)
            {
                return false;
            }

            for (int attempt = 0; attempt < MaxPolygonSampleAttempts; attempt++)
            {
                var localPoint = new Vector2(
                    Mathf.Lerp(
                        polygonLocalBounds.xMin,
                        polygonLocalBounds.xMax,
                        (float)rng.NextDouble()),
                    Mathf.Lerp(
                        polygonLocalBounds.yMin,
                        polygonLocalBounds.yMax,
                        (float)rng.NextDouble()));

                if (!ContainsAnyPath(localPoint))
                {
                    continue;
                }

                position = areaShape.transform.TransformPoint(localPoint);
                return true;
            }

            position = areaShape.transform.TransformPoint(FirstCachedPolygonPoint());
            return true;
        }

        private bool ContainsAnyPath(Vector2 localPoint)
        {
            for (int i = 0; i < polygonPaths.Length; i++)
            {
                if (ContainsPoint(polygonPaths[i], localPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private Vector2 FirstCachedPolygonPoint()
        {
            for (int pathIndex = 0; pathIndex < polygonPaths.Length; pathIndex++)
            {
                if (polygonPaths[pathIndex].Length > 0)
                {
                    return polygonPaths[pathIndex][0];
                }
            }

            return polygonLocalBounds.center;
        }

        private static bool ContainsPoint(Vector2[] polygon, Vector2 point)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }

            bool inside = false;
            int previous = polygon.Length - 1;
            for (int current = 0; current < polygon.Length; current++)
            {
                Vector2 currentPoint = polygon[current];
                Vector2 previousPoint = polygon[previous];
                bool crossesY = (currentPoint.y > point.y) != (previousPoint.y > point.y);
                if (crossesY)
                {
                    float intersectX = (previousPoint.x - currentPoint.x)
                        * (point.y - currentPoint.y)
                        / (previousPoint.y - currentPoint.y)
                        + currentPoint.x;
                    if (point.x < intersectX)
                    {
                        inside = !inside;
                    }
                }

                previous = current;
            }

            return inside;
        }

        private void ResolveAreaShape()
        {
            if (areaShape == null)
            {
                areaShape = GetComponent<PolygonCollider2D>();
            }
        }

        private static void DrawPolygonGizmo(PolygonCollider2D shape)
        {
            Vector2 offset = shape.offset;
            for (int pathIndex = 0; pathIndex < shape.pathCount; pathIndex++)
            {
                Vector2[] path = shape.GetPath(pathIndex);
                if (path == null || path.Length < 2)
                {
                    continue;
                }

                for (int pointIndex = 0; pointIndex < path.Length; pointIndex++)
                {
                    Vector2 current = path[pointIndex] + offset;
                    Vector2 next = path[(pointIndex + 1) % path.Length] + offset;
                    Gizmos.DrawLine(
                        shape.transform.TransformPoint(current),
                        shape.transform.TransformPoint(next));
                }
            }
        }
    }
}
