using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Unity.Cinemachine;

[DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
public sealed class PlayerSkillAttackReceiver : MonoBehaviour, INotificationReceiver
{
    public const int DamageFieldCount = 12;

    [SerializeField] private CinemachineImpulseSource _skillHitImpulse;
    [SerializeField, Min(0f)] private float _skillHitShakeForce = 1.8f;

    [SerializeField] private PlayerAttackInstanceContainer _attackContainer;
    [SerializeField] private Transform _hitboxRoot;
    [SerializeField] private AttackDamageField[] _damageFields = new AttackDamageField[DamageFieldCount];
    private PlayableDirector _director;

    public AttackDamageField GetDamageField(int number) =>
        _damageFields != null && number >= 1 && number <= _damageFields.Length ? _damageFields[number - 1] : null;

    private void Awake()
    {
        _director = GetComponent<PlayableDirector>();
        if (_attackContainer == null)
            _attackContainer = GetComponentInParent<PlayerAttackInstanceContainer>();
        foreach (AttackDamageField field in _damageFields)
            field?.DisablePhysicalCollision();
    }

    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (!Application.isPlaying || !isActiveAndEnabled ||
            notification is not PlayerSkillHitMarker marker ||
            _director == null || _director.state != PlayState.Playing ||
            marker.parent is not PlayerSkillHitTrack ||
            marker.parent.timelineAsset != _director.playableAsset ||
            !origin.IsValid() || !ReferenceEquals(origin.GetGraph().GetResolver(), _director))
            return;

        AttackDamageField field = GetDamageField(marker.DamageFieldNumber);
        if (_attackContainer == null || field == null || field.Hitbox == null)
        {
            Debug.LogWarning($"Skill Hit{marker.DamageFieldNumber}: Attack Container / Damage Field / Hitbox 연결을 확인하세요.", this);
            return;
        }

        Physics.SyncTransforms();

        // 시네머신 셰이크
        if (_skillHitImpulse != null)
            _skillHitImpulse.GenerateImpulseWithForce(_skillHitShakeForce);

        _attackContainer.GiveDamageFieldNoHashing(field);
    }
}
