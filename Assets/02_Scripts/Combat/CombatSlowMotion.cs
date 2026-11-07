using UnityEngine;

// 한 요청의 진입/유지/복귀. 게임 상태와 독립적으로 실제 시간으로 진행합니다.
public sealed class CombatSlowMotion
{
    // A delayed request may be ticked before Begin. Its idle endpoint must be
    // normal speed so the first fade starts at 1, rather than the default 0.
    private float _start = 1f;
    private float _target = 1f;
    private float _duration;
    private float _elapsed;
    private AnimationCurve _curve;
    private bool _releasing;

    public float Scale { get; private set; } = 1f;
    public bool IsComplete => _releasing && _elapsed >= _duration;

    public void Begin(PerfectDodgeSettings settings)
    {
        _releasing = false;
        BlendTo(Mathf.Clamp(settings.SlowScale, 0.01f, 1f), settings.FadeInDuration, settings.FadeInCurve);
    }

    public void Release(PerfectDodgeSettings settings)
    {
        if (_releasing)
            return;
        _releasing = true;
        BlendTo(1f, settings.FadeOutDuration, settings.FadeOutCurve);
    }

    public void Tick(float realDeltaTime)
    {
        _elapsed = Mathf.Min(_elapsed + Mathf.Max(0f, realDeltaTime), _duration);
        float progress = _duration > 0f ? _elapsed / _duration : 1f;
        float weight = _curve != null && _curve.length > 0 ? _curve.Evaluate(progress) : progress;
        Scale = progress >= 1f ? _target : Mathf.Lerp(_start, _target, Mathf.Clamp01(weight));
    }

    private void BlendTo(float target, float duration, AnimationCurve curve)
    {
        _start = Scale;
        _target = target;
        _duration = Mathf.Max(0f, duration);
        _elapsed = 0f;
        _curve = curve;
        if (_duration == 0f)
            Scale = _target;
    }
}
