using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class GlobalVolumeEffectController : MonoBehaviour
{
    [SerializeField] private PlayerCore _player;

    [Header("Player Dodge Effect")]
    [SerializeField, Range(0f, 1f)] private float _vignetteIntensity = 0.45f;
    [SerializeField, Min(0f), Tooltip("최대 Intensity를 유지하는 시간입니다. 실제 초 기준입니다.")]
    private float _vignetteDuration = 0.5f;
    [SerializeField, Min(0f)] private float _vignetteFadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float _vignetteFadeOutDuration = 0.3f;
    [SerializeField, Tooltip("X: 정규화된 시간(0~1), Y: 현재 강도에서 최대 강도로의 진행률(0~1)")]
    private AnimationCurve _vignetteFadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Tooltip("X: 정규화된 시간(0~1), Y: 최대 강도에서 0으로의 진행률(0~1)")]
    private AnimationCurve _vignetteFadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vignette _vignette;
    private Coroutine _vignetteRoutine;
    private PlayerCore _subscribedPlayer;

    private void Awake()
    {
        // profile은 이 Volume 전용 런타임 복사본이므로 원본 Profile 에셋을 수정하지 않습니다.
        VolumeProfile profile = GetComponent<Volume>().profile;
        if (!profile.TryGet(out _vignette))
            _vignette = profile.Add<Vignette>();

        _vignette.intensity.Override(0f);
    }

    private void OnEnable()
    {
        if (_player == null)
            _player = FindAnyObjectByType<PlayerCore>();

        _subscribedPlayer = _player;
        if (_subscribedPlayer != null)
            _subscribedPlayer.OnPerfectDodgeStarted += PerfectDodgeVolumeEffectStart;
        else
            Debug.LogWarning("완벽 회피 Vignette를 재생할 PlayerCore를 지정해 주세요.", this);
    }

    private void OnDisable()
    {
        if (_subscribedPlayer != null)
            _subscribedPlayer.OnPerfectDodgeStarted -= PerfectDodgeVolumeEffectStart;
        _subscribedPlayer = null;

        if (_vignetteRoutine != null)
            StopCoroutine(_vignetteRoutine);
        _vignetteRoutine = null;

        if (_vignette != null)
            _vignette.intensity.value = 0f;
    }

    private void PerfectDodgeVolumeEffectStart(EnemyCore enemy)
    {
        if (_subscribedPlayer != null && _subscribedPlayer.DodgeDefinition != null) return;
        if (!isActiveAndEnabled || _vignette == null)
            return;

        if (_vignetteRoutine != null)
            StopCoroutine(_vignetteRoutine);

        _vignette.active = true;
        _vignette.intensity.overrideState = true;
        _vignetteRoutine = StartCoroutine(PlayPerfectDodgeVignette());
    }

    private IEnumerator PlayPerfectDodgeVignette()
    {
        // 연속 회피 시 0으로 튀지 않고 현재 강도에서 다시 최대 강도로 올라갑니다.
        float peakIntensity = Mathf.Clamp01(_vignetteIntensity);
        yield return FadeVignette(_vignette.intensity.value, peakIntensity,
            _vignetteFadeInDuration, _vignetteFadeInCurve);

        if (_vignetteDuration > 0f)
            yield return new WaitForSecondsRealtime(_vignetteDuration);

        yield return FadeVignette(peakIntensity, 0f,
            _vignetteFadeOutDuration, _vignetteFadeOutCurve);
        _vignetteRoutine = null;
    }

    private IEnumerator FadeVignette(float from, float to, float duration, AnimationCurve curve)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            float weight = curve != null && curve.length > 0 ? curve.Evaluate(progress) : progress;
            _vignette.intensity.value = Mathf.Lerp(from, to, weight);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        // 시간 0도 처리하며, 커브의 마지막 키와 무관하게 목표 강도에 정확히 도달합니다.
        _vignette.intensity.value = to;
    }
}
