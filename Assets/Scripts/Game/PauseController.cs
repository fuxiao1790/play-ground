using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayGround.Game
{
    public interface IPauseCancelHandler
    {
        bool TryHandlePauseCancel();
    }

    public sealed class PauseController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;

        private readonly List<IPauseCancelHandler> cancelHandlers = new();
        private InputAction cancelAction;

        public bool IsPaused { get; private set; }
        public event Action<bool> PausedChanged;

        private void Awake()
        {
            if (inputActions == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(PauseController)} on {name} needs an inputActions reference.");
            }

            cancelAction = inputActions.FindAction("UI/Cancel", true);
        }

        private void OnEnable()
        {
            cancelAction.Enable();
        }

        private void OnDisable()
        {
            cancelAction.Disable();
            SetPaused(false);
        }

        private void Update()
        {
            if (cancelAction.WasPressedThisFrame())
            {
                HandleCancel();
            }
        }

        public void RegisterCancelHandler(IPauseCancelHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!cancelHandlers.Contains(handler))
                cancelHandlers.Add(handler);
        }

        public void UnregisterCancelHandler(IPauseCancelHandler handler)
        {
            if (handler != null)
                cancelHandlers.Remove(handler);
        }

        public void HandleCancel()
        {
            for (int i = cancelHandlers.Count - 1; i >= 0; i--)
            {
                if (cancelHandlers[i].TryHandlePauseCancel())
                    return;
            }

            TogglePause();
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            PausedChanged?.Invoke(paused);
        }

        public void TogglePause()
        {
            SetPaused(!IsPaused);
        }
    }
}
