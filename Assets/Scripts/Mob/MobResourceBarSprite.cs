using PlayGround.Game;
using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobResourceBarSprite : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private SpriteRenderer fill;

        private Transform fillTransform;
        private Vector3 fillFullScale;
        private bool aliveVisible = true;
        private bool settingsVisible = true;
        private bool settingsSubscribed;
        private bool initialized;
        private GameSettings gameSettings;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            SubscribeSettings();
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
        }

        public void SetHealth(float current, float max)
        {
            EnsureInitialized();
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            Vector3 scale = fillFullScale;
            scale.x = fillFullScale.x * ratio;
            fillTransform.localScale = scale;
        }

        public void SetAliveVisible(bool alive)
        {
            EnsureInitialized();
            if (aliveVisible == alive)
            {
                return;
            }

            aliveVisible = alive;
            ApplyRendererVisibility();
        }

        public void BindGameSettings(GameSettings settings)
        {
            EnsureInitialized();
            if (ReferenceEquals(gameSettings, settings))
            {
                return;
            }

            UnsubscribeSettings();
            gameSettings = settings;
            SubscribeSettings();
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            ValidateReferences();
            fillTransform = fill.transform;
            fillFullScale = fillTransform.localScale;
            initialized = true;
        }

        private void SubscribeSettings()
        {
            if (settingsSubscribed || gameSettings == null || !isActiveAndEnabled)
            {
                return;
            }

            gameSettings.DisplayMobHealthBarsChanged += HandleDisplaySettingChanged;
            settingsSubscribed = true;
            ApplySettingsVisible(gameSettings.DisplayMobHealthBars);
        }

        private void UnsubscribeSettings()
        {
            if (!settingsSubscribed)
            {
                return;
            }

            gameSettings.DisplayMobHealthBarsChanged -= HandleDisplaySettingChanged;
            settingsSubscribed = false;
        }

        private void HandleDisplaySettingChanged(bool visible)
        {
            ApplySettingsVisible(visible);
        }

        private void ApplySettingsVisible(bool visible)
        {
            if (settingsVisible == visible)
            {
                return;
            }

            settingsVisible = visible;
            ApplyRendererVisibility();
        }

        private void ApplyRendererVisibility()
        {
            bool visible = aliveVisible && settingsVisible;
            background.enabled = visible;
            fill.enabled = visible;
        }

        private void ValidateReferences()
        {
            if (background == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MobResourceBarSprite)} on {name} needs a background {nameof(SpriteRenderer)}.");
            }

            if (fill == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MobResourceBarSprite)} on {name} needs a fill {nameof(SpriteRenderer)}.");
            }
        }
    }
}
