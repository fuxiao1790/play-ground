using System;
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

        private UIDocument document;
        private VisualElement root;
        private VisualElement surface;
        private bool fireHeld;

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
        }

        private void OnDisable()
        {
            playerRoot?.ClearFireInput(this);
            fireHeld = false;
            surface?.RemoveFromHierarchy();
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(GameplayInputSurface)} requires a {nameof(UIDocument)} component.");

            if (playerRoot == null)
                throw new InvalidOperationException($"{nameof(GameplayInputSurface)} could not resolve a {nameof(PlayerRoot)}.");
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
            surface.CapturePointer(evt.pointerId);
            fireHeld = true;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!fireHeld || !surface.HasPointerCapture(evt.pointerId))
                return;

            surface.ReleasePointer(evt.pointerId);
            fireHeld = false;
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            fireHeld = false;
        }
    }
}
