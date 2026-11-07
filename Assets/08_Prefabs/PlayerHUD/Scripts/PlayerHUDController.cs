using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectR.UI
{
    /// <summary>Event-driven presentation of the existing PlayerCore combat state.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerHUDController : MonoBehaviour
    {
        [Header("Player binding")]
        [SerializeField] private PlayerCore _player;
        [SerializeField] private bool _autoFindPlayer = true;

        [Header("UGUI graphics")]
        public Image healthFill;
        public Image healthTrail;
        public Image skillFill;
        public Image skillIcon;
        public Image skillGaugeIcon;
        public Image dodgeChargeOne;
        public Image dodgeChargeTwo;
        public Image dodgeRechargeFill;
        public Image ultimateFill;
        public Image ultimateIcon;
        public HUDFacetGraphic ultimateKeycap;
        public RectTransform skillThresholdMarker;
        public CanvasGroup feedbackGroup;
        public CanvasGroup lowHealthGroup;
        public CanvasGroup readinessGroup;
        public CanvasGroup ultimateReadinessGroup;

        [Header("TMP text")]
        public TMP_Text healthValue;
        public TMP_Text skillValue;
        public TMP_Text skillStatus;
        public TMP_Text skillCostLabel;
        public TMP_Text feedbackLabel;
        public TMP_Text feedbackHint;
        public TMP_Text playerState;
        public TMP_Text skillKeyLabel;
        public TMP_Text dodgeKeyLabel;
        public TMP_Text dodgeValue;
        public TMP_Text dodgeStatus;
        public TMP_Text dodgeCooldown;
        public TMP_Text ultimateKeyLabel;
        public TMP_Text ultimateStatus;
        public TMP_Text ultimateCooldown;

        [Header("Unscaled animation timing")]
        [Min(0.01f)] public float fillResponse = 12f;
        [Min(0f)] public float damageTrailDelay = 0.4f;
        [Min(0.01f)] public float damageTrailSpeed = 0.55f;
        [Min(0.01f)] public float feedbackDuration = 1.15f;
        [Min(0.01f)] public float feedbackFadeDuration = 0.3f;
        [Min(0.01f)] public float pulsePeriod = 1.8f;
        [Range(0f, 1f)] public float lowHealthThreshold = 0.25f;

        [Header("Palette")]
        public Color healthNormalColor = new Color32(239, 249, 255, 255);
        public Color healthCriticalColor = new Color32(255, 119, 111, 255);
        public Color skillChargingColor = new Color32(113, 138, 154, 255);
        public Color skillReadyColor = new Color32(186, 243, 255, 255);
        public Color ultimateReadyColor = new Color32(255, 211, 138, 255);

        private PlayerCore _subscribedPlayer;
        private PlayerInput _playerInput;
        private float _healthTarget = 1f;
        private float _skillTarget;
        private float _trailDelayRemaining;
        private float _feedbackRemaining;
        private float _nextPlayerSearch;
        private bool _hasLiveSnapshot;
        private bool _skillReady;
        private bool _ultimateReady;
        private string _attackKey = "LMB";
        private string _counterPrompt = "LMB  /  COUNTER";

        public PlayerCore Player => _player;
        public bool IsSkillReady => _skillReady;
        public bool IsUltimateReady => _ultimateReady;
        public float HealthNormalized => _healthTarget;
        public float SkillNormalized => _skillTarget;

        private void OnEnable()
        {
            InputSystem.onActionChange += OnActionChange;
            if (feedbackGroup != null) feedbackGroup.alpha = 0f;
            _feedbackRemaining = 0f;
            BindPlayer(_player != null ? _player : FindPlayer());
        }

        private void Start()
        {
            // All PlayerCore.Awake calls have now completed, even when objects were
            // instantiated by a different component during startup.
            if (_player != null) SynchronizeFromPlayer();
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
            Unsubscribe();
            _feedbackRemaining = 0f;
            if (feedbackGroup != null) feedbackGroup.alpha = 0f;
        }

        private PlayerCore FindPlayer()
        {
            if (!_autoFindPlayer) return null;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (PlayerCore candidate in root.GetComponentsInChildren<PlayerCore>())
                        if (candidate.isActiveAndEnabled) return candidate;
            }
            PlayerCore fallback = FindAnyObjectByType<PlayerCore>();
            return fallback != null && fallback.isActiveAndEnabled ? fallback : null;
        }

        /// <summary>Bind explicitly for multiplayer or scenes containing several players.</summary>
        public void BindPlayer(PlayerCore player)
        {
            Unsubscribe();
            _player = player;
            _playerInput = player != null ? player.GetComponent<PlayerInput>() : null;
            _hasLiveSnapshot = false;
            _feedbackRemaining = 0f;
            if (feedbackGroup != null) feedbackGroup.alpha = 0f;
            if (feedbackHint != null) feedbackHint.text = string.Empty;

            if (player != null && (!Application.isPlaying || player.isActiveAndEnabled))
            {
                if (isActiveAndEnabled && Application.isPlaying)
                {
                    _subscribedPlayer = player;
                    player.OnHealthChange += OnHealthChanged;
                    player.OnSkillGaugeChange += OnSkillChanged;
                    player.OnDodgeChargesChanged += OnDodgeChargesChanged;
                    player.OnPerfectDodgeStarted += OnPerfectDodgeStarted;
                    player.OnPerfectDodgeEnded += OnPerfectDodgeEnded;
                    player.OnDodgeAttackStarted += OnDodgeAttackStarted;
                }
                SynchronizeFromPlayer();
                if (Application.isPlaying && player.IsPerfectDodgeWindowOpen) ShowFeedback("PERFECT DODGE");
            }
            else
            {
                ShowNoSignal(player != null ? "PLAYER OFFLINE" : "NO PLAYER");
            }
            RefreshInputLabels();
        }

        private void ShowNoSignal(string state)
        {
            _hasLiveSnapshot = false;
            SetPreview(0f, 100f, 0f);
            if (healthValue != null) healthValue.text = "-- / --";
            if (skillValue != null) skillValue.text = "--%";
            if (skillStatus != null) skillStatus.text = "NO SIGNAL";
            if (skillCostLabel != null) skillCostLabel.text = "COST --";
            if (skillThresholdMarker != null) skillThresholdMarker.gameObject.SetActive(false);
            if (healthFill != null) healthFill.color = skillChargingColor;
            if (playerState != null) playerState.text = state;
            if (lowHealthGroup != null) lowHealthGroup.alpha = 0f;
            SetDodgePreview(0, 0f);
            if (dodgeValue != null) dodgeValue.text = "-- / --";
            if (dodgeStatus != null) dodgeStatus.text = "NO SIGNAL";
            if (dodgeCooldown != null) dodgeCooldown.text = "--";
            if (dodgeRechargeFill != null) dodgeRechargeFill.fillAmount = 0f;
            SetUltimatePreview(false, false, false, 0f, 0f);
            if (ultimateStatus != null) ultimateStatus.text = "NO SIGNAL";
            _feedbackRemaining = 0f;
            if (feedbackGroup != null) feedbackGroup.alpha = 0f;
            if (feedbackHint != null) feedbackHint.text = string.Empty;
        }

        private void Unsubscribe()
        {
            if (_subscribedPlayer != null)
            {
                _subscribedPlayer.OnHealthChange -= OnHealthChanged;
                _subscribedPlayer.OnSkillGaugeChange -= OnSkillChanged;
                _subscribedPlayer.OnDodgeChargesChanged -= OnDodgeChargesChanged;
                _subscribedPlayer.OnPerfectDodgeStarted -= OnPerfectDodgeStarted;
                _subscribedPlayer.OnPerfectDodgeEnded -= OnPerfectDodgeEnded;
                _subscribedPlayer.OnDodgeAttackStarted -= OnDodgeAttackStarted;
            }
            _subscribedPlayer = null;
        }

        private void SynchronizeFromPlayer()
        {
            if (_player == null || (Application.isPlaying && !_player.isActiveAndEnabled)) return;
            float health = Application.isPlaying ? _player.CurrentHealth : _player.MaxHealth;
            float gauge = Application.isPlaying ? _player.CurrentSkillGauge : 0f;
            SetPreview(health, _player.MaxHealth, gauge, _player.MaxSkillGauge, _player.SkillGaugeCost);
            SetDodgePreview(Application.isPlaying ? _player.DodgeCharges : _player.MaxDodgeCharges,
                Application.isPlaying ? _player.DodgeCooldownRemaining : 0f, _player.DodgeCooldownDuration);
            SynchronizeUltimate();
            _hasLiveSnapshot = true;
        }

        /// <summary>Immediate visual-only state for prefab authoring and validation; never edits combat data.</summary>
        public void SetPreview(float health, float maxHealth, float gauge, float maxGauge = 100f, float skillCost = 50f)
        {
            UpdateHealth(maxHealth, health, true);
            UpdateSkill(maxGauge, gauge, gauge >= skillCost, true);
            UpdateSkillThreshold(maxGauge, skillCost);
            _trailDelayRemaining = 0f;
        }

        private void UpdateSkillThreshold(float maximum, float cost)
        {
            if (skillThresholdMarker != null)
            {
                skillThresholdMarker.gameObject.SetActive(true);
                float threshold = maximum > 0f ? Mathf.Clamp01(cost / maximum) : 0f;
                Vector2 min = skillThresholdMarker.anchorMin;
                Vector2 max = skillThresholdMarker.anchorMax;
                min.x = max.x = threshold;
                skillThresholdMarker.anchorMin = min;
                skillThresholdMarker.anchorMax = max;
                Vector2 position = skillThresholdMarker.anchoredPosition;
                position.x = 0f;
                skillThresholdMarker.anchoredPosition = position;
            }
            if (skillCostLabel != null) skillCostLabel.SetText("COST {0:0}", Mathf.Max(0f, cost));
        }

        private void OnHealthChanged(float maximum, float current) => UpdateHealth(maximum, current, false);

        private void OnDodgeChargesChanged(int charges, float remaining)
        {
            SetDodgePreview(charges, remaining, _player != null ? _player.DodgeCooldownDuration : 1f);
        }

        /// <summary>Only presents the shared recharge; never advances or modifies gameplay cooldown.</summary>
        public void SetDodgePreview(int charges, float remaining, float duration = 1f)
        {
            charges = Mathf.Clamp(charges, 0, 2);
            remaining = Mathf.Max(0f, remaining);
            Color empty = new Color(skillChargingColor.r, skillChargingColor.g, skillChargingColor.b, .3f);
            Color accent = charges > 0 ? skillReadyColor : healthCriticalColor;
            if (dodgeChargeOne != null) dodgeChargeOne.color = charges >= 1 ? skillReadyColor : empty;
            if (dodgeChargeTwo != null) dodgeChargeTwo.color = charges >= 2 ? skillReadyColor : empty;
            if (dodgeValue != null)
            {
                dodgeValue.SetText("{0:0} / 2", charges);
                dodgeValue.color = accent;
            }
            if (dodgeStatus != null)
            {
                dodgeStatus.text = charges > 0 ? "AVAILABLE" : "RECHARGING";
                dodgeStatus.color = accent;
            }
            if (dodgeRechargeFill != null)
            {
                dodgeRechargeFill.fillAmount = remaining > 0f ? 1f - Mathf.Clamp01(remaining / Mathf.Max(.01f, duration)) : 1f;
                dodgeRechargeFill.color = accent;
            }
            if (dodgeCooldown != null)
            {
                dodgeCooldown.color = accent;
                if (remaining <= 0f) dodgeCooldown.text = "READY";
                else
                {
                    // Round upward so a nonzero remaining cooldown never reads 0.0s.
                    int tenths = Mathf.CeilToInt(remaining * 10f);
                    dodgeCooldown.SetText("{0:0}.{1:0}s", tenths / 10, tenths % 10);
                }
            }
        }

        private void SynchronizeUltimate()
        {
            if (_player == null || (Application.isPlaying && !_player.isActiveAndEnabled)) return;
            PlayerUltimateRuntime runtime = Application.isPlaying ? _player.Ultimate : null;
            bool active = runtime != null && runtime.IsActive;
            PlayerUltimateDefinition definition = active ? runtime.ActiveDefinition : _player.UltimateDefinition;
            bool configured = definition != null && definition.Ready;
            SetUltimatePreview(configured, active, Application.isPlaying ? _player.CanBeginUltimate : configured,
                runtime != null ? runtime.CooldownRemaining : 0f,
                runtime != null ? runtime.CooldownDuration : definition != null ? definition.cooldown : 0f);
        }

        /// <summary>Presents the runtime snapshot without advancing or changing the ultimate cooldown.</summary>
        public void SetUltimatePreview(bool configured, bool active, bool ready, float remaining, float duration)
        {
            remaining = Mathf.Max(0f, remaining);
            _ultimateReady = configured && !active && remaining <= 0f && ready;
            bool highlighted = configured && (active || _ultimateReady);
            Color accent = highlighted ? ultimateReadyColor : skillChargingColor;
            string status = !configured ? "UNAVAILABLE" : active ? "ACTIVE" : remaining > 0f ? "COOLDOWN"
                : _ultimateReady ? "READY" : "WAIT";
            if (ultimateFill != null)
            {
                ultimateFill.fillAmount = !configured ? 0f : remaining > 0f
                    ? 1f - Mathf.Clamp01(remaining / Mathf.Max(.01f, duration)) : 1f;
                ultimateFill.color = accent;
            }
            if (ultimateIcon != null) ultimateIcon.color = accent;
            if (ultimateKeycap != null)
            {
                if (ultimateKeycap.borderColor != accent)
                {
                    ultimateKeycap.borderColor = accent;
                    ultimateKeycap.SetVerticesDirty();
                }
                ultimateKeycap.color = highlighted ? ultimateReadyColor : new Color(.10f, .17f, .21f, .92f);
            }
            if (ultimateKeyLabel != null)
                ultimateKeyLabel.color = highlighted ? new Color(.025f, .045f, .06f, 1f) : skillChargingColor;
            if (ultimateStatus != null)
            {
                ultimateStatus.text = status;
                ultimateStatus.color = accent;
            }
            if (ultimateCooldown != null)
            {
                ultimateCooldown.color = accent;
                if (!configured) ultimateCooldown.text = "--";
                else if (remaining > 0f)
                {
                    int tenths = Mathf.CeilToInt(remaining * 10f);
                    ultimateCooldown.SetText("{0:0}.{1:0}s", tenths / 10, tenths % 10);
                }
                else ultimateCooldown.text = string.Empty;
            }
            if (ultimateReadinessGroup != null) ultimateReadinessGroup.alpha = _ultimateReady ? 1f : 0f;
        }

        private void OnSkillChanged(float maximum, float current)
        {
            UpdateSkill(maximum, current, _player != null && _player.IsSkillEnable, false);
            if (_player != null) UpdateSkillThreshold(maximum, _player.SkillGaugeCost);
        }

        private void UpdateHealth(float maximum, float current, bool immediate)
        {
            maximum = Mathf.Max(0f, maximum);
            current = Mathf.Clamp(current, 0f, maximum);
            float next = maximum > 0f ? current / maximum : 0f;
            if (!immediate && next < _healthTarget)
                _trailDelayRemaining = damageTrailDelay;
            if (immediate || next > _healthTarget)
            {
                if (healthTrail != null) healthTrail.fillAmount = next;
                _trailDelayRemaining = 0f;
            }
            _healthTarget = next;
            if (immediate && healthFill != null) healthFill.fillAmount = next;
            bool critical = next <= lowHealthThreshold;
            if (healthFill != null) healthFill.color = critical ? healthCriticalColor : healthNormalColor;
            if (healthValue != null) healthValue.SetText("{0:0} / {1:0}", current, maximum);
            if (playerState != null) playerState.text = current <= 0f ? "VITALS ZERO" : critical ? "CRITICAL" : "VITALS STABLE";
            if (lowHealthGroup != null) lowHealthGroup.alpha = critical ? 0.8f : 0f;
        }

        private void UpdateSkill(float maximum, float current, bool ready, bool immediate)
        {
            _skillTarget = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            _skillReady = ready;
            if (skillFill != null)
            {
                if (immediate) skillFill.fillAmount = _skillTarget;
                skillFill.color = ready ? skillReadyColor : skillChargingColor;
            }
            if (skillIcon != null) skillIcon.color = ready ? skillReadyColor : skillChargingColor;
            if (skillGaugeIcon != null) skillGaugeIcon.color = ready ? skillReadyColor : skillChargingColor;
            if (skillValue != null) skillValue.SetText("{0:0}%", _skillTarget * 100f);
            if (skillStatus != null)
            {
                skillStatus.text = ready ? "READY" : "CHARGING";
                skillStatus.color = ready ? skillReadyColor : skillChargingColor;
            }
            if (readinessGroup != null) readinessGroup.alpha = ready ? 1f : 0f;
        }

        private void Update()
        {
            if (_player == null)
            {
                if (_hasLiveSnapshot) BindPlayer(null);
                if (_autoFindPlayer && Time.unscaledTime >= _nextPlayerSearch)
                {
                    _nextPlayerSearch = Time.unscaledTime + 0.5f;
                    PlayerCore found = FindPlayer();
                    if (found != null) BindPlayer(found);
                }
            }
            else if (!_player.isActiveAndEnabled)
            {
                if (_hasLiveSnapshot || _subscribedPlayer != null)
                {
                    Unsubscribe();
                    ShowNoSignal("PLAYER OFFLINE");
                }
            }
            else if (_subscribedPlayer != _player)
            {
                BindPlayer(_player);
            }

            if (_hasLiveSnapshot) SynchronizeUltimate();

            float dt = Time.unscaledDeltaTime;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, fillResponse) * dt);
            if (healthFill != null) healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, _healthTarget, blend);
            if (skillFill != null) skillFill.fillAmount = Mathf.Lerp(skillFill.fillAmount, _skillTarget, blend);
            _trailDelayRemaining = Mathf.Max(0f, _trailDelayRemaining - dt);
            if (healthTrail != null && _trailDelayRemaining <= 0f)
            {
                float target = healthFill != null ? Mathf.Max(_healthTarget, healthFill.fillAmount) : _healthTarget;
                healthTrail.fillAmount = Mathf.MoveTowards(healthTrail.fillAmount, target, damageTrailSpeed * dt);
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.01f, pulsePeriod));
            if (readinessGroup != null) readinessGroup.alpha = _skillReady ? Mathf.Lerp(0.55f, 1f, pulse) : 0f;
            if (ultimateReadinessGroup != null)
                ultimateReadinessGroup.alpha = _ultimateReady ? Mathf.Lerp(0.55f, 1f, pulse) : 0f;
            if (lowHealthGroup != null)
                lowHealthGroup.alpha = _hasLiveSnapshot && _healthTarget <= lowHealthThreshold ? Mathf.Lerp(0.45f, 0.9f, pulse) : 0f;

            _feedbackRemaining = Mathf.Max(0f, _feedbackRemaining - dt);
            if (_player != null && _player.isActiveAndEnabled && _player.IsPerfectDodgeWindowOpen)
                _feedbackRemaining = Mathf.Max(_feedbackRemaining, feedbackFadeDuration);
            if (feedbackGroup != null) feedbackGroup.alpha = Mathf.Clamp01(_feedbackRemaining / Mathf.Max(0.01f, feedbackFadeDuration));
            UpdateCounterHint();
        }

        private void OnPerfectDodgeStarted(EnemyCore source)
        {
            ShowFeedback("PERFECT DODGE");
            UpdateCounterHint();
        }

        private void OnPerfectDodgeEnded()
        {
            if (feedbackHint != null) feedbackHint.text = string.Empty;
            _feedbackRemaining = Mathf.Min(_feedbackRemaining, feedbackFadeDuration);
        }

        private void OnDodgeAttackStarted(EnemyCore source)
        {
            ShowFeedback("COUNTER ATTACK");
            if (feedbackHint != null) feedbackHint.text = string.Empty;
        }

        public void ShowFeedback(string message)
        {
            if (feedbackLabel != null) feedbackLabel.text = message;
            _feedbackRemaining = Mathf.Max(feedbackFadeDuration, feedbackDuration);
            if (feedbackGroup != null) feedbackGroup.alpha = 1f;
        }

        private void UpdateCounterHint()
        {
            if (feedbackHint == null) return;
            string hint = _player != null && _player.isActiveAndEnabled && _player.IsPerfectDodgeWindowOpen ? _counterPrompt : string.Empty;
            if (feedbackHint.text != hint) feedbackHint.text = hint;
        }

        private void OnActionChange(object changed, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged) RefreshInputLabels();
        }

        public void RefreshInputLabels()
        {
            _attackKey = GetKeyLabel("Attack", "LMB");
            _counterPrompt = _attackKey + "  /  COUNTER";
            if (skillKeyLabel != null) skillKeyLabel.text = GetKeyLabel("Skill", "E");
            if (dodgeKeyLabel != null) dodgeKeyLabel.text = GetKeyLabel("Dodge", "SHIFT");
            if (ultimateKeyLabel != null) ultimateKeyLabel.text = GetKeyLabel("Ultimate", "Q");
            UpdateCounterHint();
        }

        private string GetKeyLabel(string actionName, string fallback)
        {
            InputAction action = _playerInput != null && _playerInput.actions != null
                ? _playerInput.actions.FindAction(actionName, false) : null;
            if (action == null) return fallback;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isPartOfComposite) continue;
                if (binding.isComposite)
                {
                    string composite = action.GetBindingDisplayString(i);
                    if (!string.IsNullOrWhiteSpace(composite)) return composite.ToUpperInvariant();
                    continue;
                }
                string path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (path == "<Mouse>/leftButton") return "LMB";
                if (path == "<Mouse>/rightButton") return "RMB";
                if (path == "<Keyboard>/leftShift") return "SHIFT";
                if (!path.StartsWith("<Keyboard>") && !path.StartsWith("<Mouse>")) continue;
                string label = action.GetBindingDisplayString(i);
                if (!string.IsNullOrWhiteSpace(label)) return label.ToUpperInvariant();
            }
            return "UNBOUND";
        }
    }
}
