using System;
using PlayGround.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayGround.Ui
{
    [DefaultExecutionOrder(1000)]
    public sealed class PauseMenuUi : MonoBehaviour, IUiCancelHandler
    {
        private const string HiddenClassName = "pause-menu--hidden";
        private const string OptionsHiddenClassName = "options-popup--hidden";

        [SerializeField] private UIDocument document;
        [SerializeField] private VisualTreeAsset pauseMenuTemplate;
        [SerializeField] private StyleSheet pauseMenuStyleSheet;
        [SerializeField] private PauseController pauseController;
        [SerializeField] private GameSettings gameSettings;

        private VisualElement root;
        private VisualElement pauseMenuLayer;
        private VisualElement pauseBackground;
        private VisualElement pauseMenu;
        private VisualElement optionsPopup;
        private Button resumeButton;
        private Button optionsButton;
        private Toggle displayMobHealthBarsToggle;

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
            optionsButton = pauseMenu.Q<Button>("options");
            optionsPopup = pauseMenu.Q<VisualElement>("options-popup");
            displayMobHealthBarsToggle = pauseMenu.Q<Toggle>("display-mob-health-bars");
            if (resumeButton == null
                || optionsButton == null
                || optionsPopup == null
                || displayMobHealthBarsToggle == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PauseMenuUi)} could not find the '#resume', '#options', '#options-popup', and '#display-mob-health-bars' elements in {nameof(pauseMenuTemplate)}.");
            }

            resumeButton.clicked += OnResumeClicked;
            optionsButton.clicked += OnOptionsClicked;
            displayMobHealthBarsToggle.SetValueWithoutNotify(gameSettings.DisplayMobHealthBars);
            displayMobHealthBarsToggle.RegisterValueChangedCallback(OnDisplayMobHealthBarsToggleChanged);
            gameSettings.DisplayMobHealthBarsChanged += OnDisplayMobHealthBarsChanged;
            pauseMenuLayer.Add(pauseMenu);
            pauseController.PausedChanged += OnPausedChanged;
            OnPausedChanged(pauseController.IsPaused);
        }

        private void OnDisable()
        {
            if (pauseController != null)
            {
                pauseController.PausedChanged -= OnPausedChanged;
                pauseController.UnregisterCancelHandler(this);
            }

            if (resumeButton != null)
                resumeButton.clicked -= OnResumeClicked;

            if (optionsButton != null)
                optionsButton.clicked -= OnOptionsClicked;

            if (displayMobHealthBarsToggle != null)
                displayMobHealthBarsToggle.UnregisterValueChangedCallback(OnDisplayMobHealthBarsToggleChanged);

            if (gameSettings != null)
                gameSettings.DisplayMobHealthBarsChanged -= OnDisplayMobHealthBarsChanged;

            CloseOptionsPopup();
            pauseMenu?.RemoveFromHierarchy();
            pauseMenu = null;
            optionsPopup = null;
            resumeButton = null;
            optionsButton = null;
            displayMobHealthBarsToggle = null;
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

            if (gameSettings == null)
                throw new InvalidOperationException($"{nameof(PauseMenuUi)} needs a {nameof(gameSettings)} reference.");
        }

        private void OnPausedChanged(bool paused)
        {
            if (!paused)
                CloseOptionsPopup();

            pauseMenu.EnableInClassList(HiddenClassName, !paused);
            pauseBackground.EnableInClassList("pause-background--visible", paused);
            pauseBackground.EnableInClassList("pause-background--hidden", !paused);
            pauseBackground.pickingMode = paused ? PickingMode.Position : PickingMode.Ignore;
        }

        public bool TryHandleCancel()
        {
            if (optionsPopup == null || optionsPopup.ClassListContains(OptionsHiddenClassName))
                return false;

            CloseOptionsPopup();
            return true;
        }

        private void OnResumeClicked()
        {
            pauseController.SetPaused(false);
        }

        private void OnOptionsClicked()
        {
            pauseController.RegisterCancelHandler(this);
            optionsPopup.RemoveFromClassList(OptionsHiddenClassName);
        }

        private void OnDisplayMobHealthBarsToggleChanged(ChangeEvent<bool> changeEvent)
        {
            gameSettings.SetDisplayMobHealthBars(changeEvent.newValue);
        }

        private void OnDisplayMobHealthBarsChanged(bool display)
        {
            displayMobHealthBarsToggle?.SetValueWithoutNotify(display);
        }

        private void CloseOptionsPopup()
        {
            if (pauseController != null)
                pauseController.UnregisterCancelHandler(this);

            optionsPopup?.AddToClassList(OptionsHiddenClassName);
        }
    }
}
