using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-9000)]
public sealed class CombatTimeController : MonoBehaviour
{
    private static CombatTimeController _instance;
    private readonly Dictionary<UnityEngine.Object, CombatSlowMotion> _requests = new();
    private readonly Dictionary<UnityEngine.Object, float> _evaluatedRequests = new();
    private readonly List<UnityEngine.Object> _finished = new();

    public static float Scale { get; private set; } = 1f;
    public static float DeltaTime => Time.deltaTime * Scale;
    public static event Action<float> ScaleChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        Scale = 1f;
        ScaleChanged = null;
    }

    public static void Begin(UnityEngine.Object owner, PerfectDodgeSettings settings)
    {
        if (owner == null || settings == null)
            return;
        if (_instance == null)
        {
            _instance = FindAnyObjectByType<CombatTimeController>();
            if (_instance == null)
                _instance = new GameObject(nameof(CombatTimeController)).AddComponent<CombatTimeController>();
        }

        if (!_instance._requests.TryGetValue(owner, out CombatSlowMotion request))
        {
            request = new CombatSlowMotion();
            _instance._requests.Add(owner, request);
        }
        request.Begin(settings);
        _instance.RefreshScale();
    }

    public static void End(UnityEngine.Object owner, PerfectDodgeSettings settings, bool immediate)
    {
        if (_instance == null || ReferenceEquals(owner, null))
            return;
        if (!_instance._requests.TryGetValue(owner, out CombatSlowMotion request))
            return;
        if (immediate)
            _instance._requests.Remove(owner);
        else
            request.Release(settings);
        _instance.RefreshScale();
    }

    // Dodge Studio evaluates the same envelope in runtime and preview. These requests
    // have separate owner tokens and never remove another system's slow motion.
    public static void SetEvaluatedScale(UnityEngine.Object owner, float scale)
    {
        if (owner == null) return;
        if (_instance == null)
        {
            _instance = FindAnyObjectByType<CombatTimeController>();
            if (_instance == null) _instance = new GameObject(nameof(CombatTimeController)).AddComponent<CombatTimeController>();
        }
        _instance._evaluatedRequests[owner] = Mathf.Clamp(scale, .01f, 1f);
        _instance.RefreshScale();
    }

    public static void RemoveEvaluatedScale(UnityEngine.Object owner)
    {
        if (_instance == null || ReferenceEquals(owner, null)) return;
        _instance._evaluatedRequests.Remove(owner);
        _instance.RefreshScale();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (VirtualCutscenePlayback.IsPlaying || ProjectR.UI.PauseSettingsController.IsPaused) return;
        _finished.Clear();
        foreach (var pair in _requests)
        {
            pair.Value.Tick(Time.unscaledDeltaTime);
            if (pair.Key == null || pair.Value.IsComplete)
                _finished.Add(pair.Key);
        }
        foreach (var owner in _finished)
            _requests.Remove(owner);
        _finished.Clear();
        foreach (var pair in _evaluatedRequests) if (pair.Key == null) _finished.Add(pair.Key);
        foreach (var owner in _finished) _evaluatedRequests.Remove(owner);
        RefreshScale();
    }

    private void RefreshScale()
    {
        float scale = 1f;
        foreach (var request in _requests.Values)
            scale = Mathf.Min(scale, request.Scale);
        foreach (float evaluated in _evaluatedRequests.Values) scale = Mathf.Min(scale, evaluated);
        SetScale(scale);
    }

    private static void SetScale(float scale)
    {
        if (Scale == scale)
            return;
        Scale = scale;
        ScaleChanged?.Invoke(scale);
    }

    private void OnDestroy()
    {
        if (_instance != this)
            return;
        _instance = null;
        SetScale(1f);
    }
}
