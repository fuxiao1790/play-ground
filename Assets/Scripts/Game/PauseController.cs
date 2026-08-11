using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayGround.Game
{
    public sealed class PauseController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;

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
                TogglePause();
            }
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
