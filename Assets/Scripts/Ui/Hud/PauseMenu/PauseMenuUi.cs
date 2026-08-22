using System;
using PlayGround.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.Ui
{
    [DefaultExecutionOrder(1000)]
    public sealed class PauseMenuUi : MonoBehaviour
    {
        private const string HiddenClassName = "pause-menu--hidden";

        [SerializeField] private UIDocument document;
        [SerializeField] private VisualTreeAsset pauseMenuTemplate;
        [SerializeField] private StyleSheet pauseMenuStyleSheet;
        [SerializeField] private PauseController pauseController;

        private VisualElement root;
        private VisualElement pauseMenuLayer;
        private VisualElement pauseBackground;
        private VisualElement pauseMenu;
        private Button resumeButton;

        private void Awake()
        {
            ValidateSetup();
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            pauseMenuLayer = root.Q<VisualElement>("pause-menu-layer");
            pauseBackground = root.Q<VisualElement>("pause-background");
            if (pauseMenuLayer == null || pauseBackground == null)
                throw new InvalidOperationException(
                    $"{nameof(PauseMenuUi)} could not find the '#pause-menu-layer'/'#pause-background' elements. Assign SkillLoadoutUi.uxml as the UIDocument Source Asset.");

            pauseMenu = pauseMenuTemplate.Instantiate().Q<VisualElement>("pause-menu");
            if (pauseMenu == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PauseMenuUi)} could not find the '#pause-menu' element in {nameof(pauseMenuTemplate)}.");
            }

            pauseMenu.styleSheets.Add(pauseMenuStyleSheet);
            resumeButton = pauseMenu.Q<Button>("resume");
            if (resumeButton == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PauseMenuUi)} could not find the '#resume' element in {nameof(pauseMenuTemplate)}.");
            }

            resumeButton.clicked += OnResumeClicked;
            pauseMenuLayer.Add(pauseMenu);
            pauseController.PausedChanged += OnPausedChanged;
            OnPausedChanged(pauseController.IsPaused);
        }

        private void OnDisable()
        {
            if (pauseController != null)
                pauseController.PausedChanged -= OnPausedChanged;

            if (resumeButton != null)
                resumeButton.clicked -= OnResumeClicked;

            pauseMenu?.RemoveFromHierarchy();
            pauseMenu = null;
            resumeButton = null;
            pauseMenuLayer = null;
            pauseBackground = null;
            root = null;
        }

        private void ValidateSetup()
        {
            if (document == null)
                throw new InvalidOperationException($"{nameof(PauseMenuUi)} needs a {nameof(document)} reference.");

            if (pauseMenuTemplate == null)
                throw new InvalidOperationException($"{nameof(PauseMenuUi)} needs a {nameof(pauseMenuTemplate)} reference.");

            if (pauseMenuStyleSheet == null)
                throw new InvalidOperationException($"{nameof(PauseMenuUi)} needs a {nameof(pauseMenuStyleSheet)} reference.");

            if (pauseController == null)
                throw new InvalidOperationException($"{nameof(PauseMenuUi)} needs a {nameof(pauseController)} reference.");
        }

        private void OnPausedChanged(bool paused)
        {
            pauseMenu.EnableInClassList(HiddenClassName, !paused);
            pauseBackground.EnableInClassList("pause-background--visible", paused);
            pauseBackground.EnableInClassList("pause-background--hidden", !paused);
            pauseBackground.pickingMode = paused ? PickingMode.Position : PickingMode.Ignore;
        }

        private void OnResumeClicked()
        {
            pauseController.SetPaused(false);
        }
    }
}
