using System;
using PlayGround.Game;
using PlayGround.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.Skills
{
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameplayInputSurface : MonoBehaviour, IGameplayInputSource
    {
        [SerializeField] private PlayerRoot playerRoot;
        [SerializeField] private PauseController pauseController;

        private UIDocument document;
        private VisualElement root;
        private VisualElement surface;
        private bool fireHeld;
        private int? capturedPointerId;

        public bool FireHeld => fireHeld;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            if (playerRoot == null) playerRoot = FindAnyObjectByType<PlayerRoot>();
            ValidateSetup();
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            CreateSurfaceIfNeeded();
            InsertSurfaceAtRootStart();
            playerRoot.SetFireInput(this);
            pauseController.PausedChanged += OnPausedChanged;
            OnPausedChanged(pauseController.IsPaused);
        }

        private void OnDisable()
        {
            pauseController.PausedChanged -= OnPausedChanged;
            playerRoot?.ClearFireInput(this);
            ClearHeldPointer();
            surface?.RemoveFromHierarchy();
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(GameplayInputSurface)} requires a {nameof(UIDocument)} component.");

            if (playerRoot == null)
                throw new InvalidOperationException($"{nameof(GameplayInputSurface)} could not resolve a {nameof(PlayerRoot)}.");

            if (pauseController == null)
                throw new InvalidOperationException($"{nameof(GameplayInputSurface)} needs a {nameof(PauseController)}.");
        }

        private void CreateSurfaceIfNeeded()
        {
            if (surface != null)
                return;

            surface = new VisualElement();
            surface.AddToClassList("world-input-surface");
            surface.pickingMode = PickingMode.Position;
            surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
            surface.RegisterCallback<PointerUpEvent>(OnPointerUp);
            surface.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void InsertSurfaceAtRootStart()
        {
            if (surface.parent == root)
            {
                if (root.IndexOf(surface) == 0)
                    return;

                surface.RemoveFromHierarchy();
            }
            else
            {
                surface.RemoveFromHierarchy();
            }

            root.Insert(0, surface);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (pauseController.IsPaused)
                return;

            surface.CapturePointer(evt.pointerId);
            capturedPointerId = evt.pointerId;
            fireHeld = true;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!fireHeld || capturedPointerId != evt.pointerId || !surface.HasPointerCapture(evt.pointerId))
                return;

            ClearHeldPointer();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (capturedPointerId == evt.pointerId)
            {
                capturedPointerId = null;
                fireHeld = false;
            }
        }

        private void OnPausedChanged(bool paused)
        {
            surface.pickingMode = paused ? PickingMode.Ignore : PickingMode.Position;
            if (paused)
                ClearHeldPointer();
        }

        private void ClearHeldPointer()
        {
            int? pointerId = capturedPointerId;
            capturedPointerId = null;
            fireHeld = false;
            if (pointerId.HasValue && surface != null && surface.HasPointerCapture(pointerId.Value))
                surface.ReleasePointer(pointerId.Value);
        }
    }
}
