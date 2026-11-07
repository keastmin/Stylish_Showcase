using UnityEngine;
using UnityEngine.Playables;

/// <summary>Timing shared by the E Skill Studio and its gameplay state.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
public sealed class PlayerSkillSequenceSettings : MonoBehaviour
{
    [SerializeField, Tooltip("E 스킬의 이동·타격·화면 효과·입력 가능 시각을 정의합니다. 비우면 기존 Timeline 로직을 사용합니다.")]
    private PlayerSkillDefinition _definition;
    [SerializeField, Tooltip("마지막 Timeline 클립까지 스킬을 재생합니다.")]
    private bool _useTimelineDuration = true;
    [SerializeField, Min(0.01f), Tooltip("자동 종료를 끈 경우의 종료 시각(초). Timeline 길이를 넘지 않습니다.")]
    private float _endTime = 2.2f;
    [SerializeField, Min(0f), Tooltip("Timeline 종료 후 조작이 돌아올 때까지의 전투 시간(초). 히트스톱 중에는 멈춥니다.")]
    private float _recoveryDuration;

    public PlayerSkillDefinition Definition { get => _definition; set => _definition = value; }
    public bool UseTimelineDuration { get => _useTimelineDuration; set => _useTimelineDuration = value; }
    public float EndTime { get => _endTime; set => _endTime = FinitePositive(value, 2.2f, .01f); }
    public float RecoveryDuration { get => FinitePositive(_recoveryDuration, 0f, 0f); set => _recoveryDuration = FinitePositive(value, 0f, 0f); }

    public double GetContentEndTime(PlayableDirector director)
    {
        double duration = TimelineDuration(director);
        return _useTimelineDuration ? duration : System.Math.Min(duration, FinitePositive(_endTime, 2.2f, .01f));
    }

    public double GetEndTime(PlayableDirector director) => GetContentEndTime(director) + RecoveryDuration;

    public static double TimelineDuration(PlayableDirector director)
    {
        double duration = director != null && director.playableAsset != null ? director.playableAsset.duration : 0d;
        return !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d ? duration : 2.2d;
    }

    private static float FinitePositive(float value, float fallback, float minimum)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

    private void OnValidate()
    {
        EndTime = _endTime;
        RecoveryDuration = _recoveryDuration;
    }
}
