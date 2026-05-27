using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayGround.CameraSystem
{
    [RequireComponent(typeof(Camera))]
    public sealed class GameplayCamera : MonoBehaviour
    {
        private const float DebugLimitExtent = 10000000f;

        [SerializeField] private Transform target;
        [SerializeField, Range(0f, 1f)] private float followSpeed = 1f;
        [SerializeField, Range(0f, 1f)] private float mouseBias = 0.3f;
        [SerializeField, Range(0f, 1f)] private float ovalWidth = 0.15f;
        [SerializeField, Range(0f, 1f)] private float ovalHeight = 0.15f;
        [SerializeField] private float baseOrthographicSize = 9f;
        [SerializeField] private float initialZoom = 1.5f;
        [SerializeField] private float zoomStep = 0.1f;
        [SerializeField] private float zoomMin = 0.25f;
        [SerializeField] private float zoomMax = 4f;
        [SerializeField, Range(0f, 1f)] private float zoomSpeed = 0.2f;
        [SerializeField] private Rect releaseLimits = new(-12f, -7f, 24f, 14f);
        [SerializeField] private bool useReleaseLimits = true;
        [SerializeField] private bool allowBeyondLimitsInDebug = true;

        private Camera attachedCamera;
        private float currentZoom;
        private float targetZoom;

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            attachedCamera.orthographic = true;
            currentZoom = Mathf.Clamp(initialZoom, zoomMin, zoomMax);
            targetZoom = currentZoom;
            ApplyZoom();
        }

        private void Start()
        {
            if (target == null)
            {
                throw new MissingReferenceException($"{nameof(GameplayCamera)} on {name} needs a target.");
            }
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (scroll > 0f)
            {
                targetZoom = Mathf.Clamp(targetZoom * (1f + zoomStep), zoomMin, zoomMax);
            }
            else if (scroll < 0f)
            {
                targetZoom = Mathf.Clamp(targetZoom / (1f + zoomStep), zoomMin, zoomMax);
            }
        }

        private void LateUpdate()
        {
            Vector2 targetPosition = target.position;
            Vector2 cameraPosition = transform.position;
            Vector2 bounds = OvalHalfExtentsWorld();
            Vector2 offset = targetPosition - cameraPosition;
            float ellipse = EllipseDistance(offset, bounds);
            Vector2 nextPosition;

            if (ellipse > 1f)
            {
                Vector2 clamped = offset / Mathf.Sqrt(ellipse);
                nextPosition = targetPosition - clamped;
            }
            else
            {
                nextPosition = Vector2.Lerp(cameraPosition, DesiredPosition(targetPosition, bounds), followSpeed * Time.deltaTime);
            }

            currentZoom += (targetZoom - currentZoom) * zoomSpeed;
            ApplyZoom();
            nextPosition = ClampToLimits(nextPosition);
            transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
        }

        public void Configure(Transform followTarget)
        {
            target = followTarget;
        }

        public void Configure(Transform followTarget, Rect cameraLimits)
        {
            target = followTarget;
            releaseLimits = cameraLimits;
            useReleaseLimits = true;
        }

        public static Vector2 ClampPlayerToOval(Vector2 cameraPosition, Vector2 targetPosition, Vector2 ovalHalfExtents)
        {
            Vector2 offset = targetPosition - cameraPosition;
            float ellipse = EllipseDistance(offset, ovalHalfExtents);
            if (ellipse <= 1f)
            {
                return cameraPosition;
            }

            Vector2 clamped = offset / Mathf.Sqrt(ellipse);
            return targetPosition - clamped;
        }

        private Vector2 DesiredPosition(Vector2 targetPosition, Vector2 ovalHalfExtents)
        {
            Vector2 desired = targetPosition + (MouseWorldPosition() - targetPosition) * mouseBias;
            Vector2 targetOffset = targetPosition - desired;
            float ellipse = EllipseDistance(targetOffset, ovalHalfExtents);
            if (ellipse <= 1f)
            {
                return desired;
            }

            Vector2 clamped = targetOffset / Mathf.Sqrt(ellipse);
            return targetPosition - clamped;
        }

        private Vector2 MouseWorldPosition()
        {
            Mouse mouse = Mouse.current;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 world = attachedCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -attachedCamera.transform.position.z));
            return world;
        }

        private Vector2 OvalHalfExtentsWorld()
        {
            float halfHeight = attachedCamera.orthographicSize;
            float halfWidth = halfHeight * attachedCamera.aspect;
            return new Vector2(ovalWidth * halfWidth * 2f, ovalHeight * halfHeight * 2f);
        }

        private void ApplyZoom()
        {
            attachedCamera.orthographicSize = baseOrthographicSize / Mathf.Max(currentZoom, 0.0001f);
        }

        private Vector2 ClampToLimits(Vector2 position)
        {
            if (!useReleaseLimits)
            {
                return position;
            }

            Rect limits = EffectiveLimits();
            float halfHeight = attachedCamera.orthographicSize;
            float halfWidth = halfHeight * attachedCamera.aspect;
            float minX = limits.xMin + halfWidth;
            float maxX = limits.xMax - halfWidth;
            float minY = limits.yMin + halfHeight;
            float maxY = limits.yMax - halfHeight;

            float x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : limits.center.x;
            float y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : limits.center.y;
            return new Vector2(x, y);
        }

        private Rect EffectiveLimits()
        {
            if (allowBeyondLimitsInDebug && Debug.isDebugBuild)
            {
                return new Rect(-DebugLimitExtent, -DebugLimitExtent, DebugLimitExtent * 2f, DebugLimitExtent * 2f);
            }

            return releaseLimits;
        }

        private static float EllipseDistance(Vector2 offset, Vector2 halfExtents)
        {
            float halfWidth = Mathf.Max(halfExtents.x, 0.0001f);
            float halfHeight = Mathf.Max(halfExtents.y, 0.0001f);
            float x = offset.x / halfWidth;
            float y = offset.y / halfHeight;
            return x * x + y * y;
        }
    }
}
