using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Level
{
    public sealed class PlayAreaRoot : MonoBehaviour
    {
        [SerializeField] private Vector2 size = new(24f, 14f);
        [SerializeField] private float wallThickness = 0.5f;

        private void Awake()
        {
            int environmentLayer = GameplayLayers.RequiredLayer(GameplayLayers.Environment);
            gameObject.layer = environmentLayer;
        }

        public Rect Bounds => new(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);

        public void Configure(Vector2 arenaSize, float thickness)
        {
            size = arenaSize;
            wallThickness = thickness;
        }

        public void BuildRuntimeWalls()
        {
            int environmentLayer = GameplayLayers.RequiredLayer(GameplayLayers.Environment);
            CreateWall("NorthWall", new Vector2(0f, size.y * 0.5f), new Vector2(size.x, wallThickness), environmentLayer);
            CreateWall("SouthWall", new Vector2(0f, -size.y * 0.5f), new Vector2(size.x, wallThickness), environmentLayer);
            CreateWall("EastWall", new Vector2(size.x * 0.5f, 0f), new Vector2(wallThickness, size.y), environmentLayer);
            CreateWall("WestWall", new Vector2(-size.x * 0.5f, 0f), new Vector2(wallThickness, size.y), environmentLayer);
        }

        private void CreateWall(string wallName, Vector2 position, Vector2 wallSize, int layer)
        {
            Transform existing = transform.Find(wallName);
            GameObject wall = existing != null ? existing.gameObject : new GameObject(wallName);
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = position;
            wall.layer = layer;

            BoxCollider2D collider = wall.GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                collider = wall.AddComponent<BoxCollider2D>();
            }

            collider.size = wallSize;
        }
    }
}
