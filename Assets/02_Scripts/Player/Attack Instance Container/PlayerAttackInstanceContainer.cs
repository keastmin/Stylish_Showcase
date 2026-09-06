using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackInstanceContainer : MonoBehaviour
{
    [SerializeField] private AttackDamageField _basicAttack1DamageField;
    [SerializeField] private AttackDamageField _basicAttack2DamageField;
    [SerializeField] private AttackDamageField _basicAttack3DamageField;
    [SerializeField] private AttackDamageField _basicAttack4DamageField;
    [SerializeField] private AttackDamageField _runAttack1HitDamageField;
    [SerializeField] private AttackDamageField _runAttack2HitDamageField;
    [SerializeField] private AttackDamageField _dodgeAttack1HitDamageField;
    [SerializeField] private AttackDamageField _dodgeAttack2HitDamageField;
    [SerializeField] private AttackDamageField _dodgeAttackEndDamageField;
    [SerializeField] private AttackDamageField _dodgeAttackEndRangeDamageField;

    [SerializeField] private Transform _basicAttack2GroundCrackTransform;
    [SerializeField] private ParticleSystem _basicAttack2GroundCrackParticle;

    private readonly HashSet<IDamageable> _damagedTargets = new();
    private readonly HashSet<IDamageable> _targetsInCurrentDetection = new();
    private readonly List<IHitStopParticipant> _hitStopVictims = new();

    private IHitStopParticipant _ownerHitStopParticipant;
    private PlayerSFXController _playerSFXController;

    public event Action<float> OnAttackSkillGaugeAdditive; // 타격에 의한 스킬 게이지 증가 이벤트

    private void Awake()
    {
        _ownerHitStopParticipant = GetComponentInParent<IHitStopParticipant>();
        PlayerCore owner = GetComponentInParent<PlayerCore>();
        _playerSFXController = owner != null
            ? owner.GetComponentInChildren<PlayerSFXController>(true)
            : GetComponentInParent<PlayerSFXController>();
    }

    // 기본 공격 1 데미지 주기
    public void OnGiveDamageBasicAttack1()
    {
        GiveDamageFieldHashing(_basicAttack1DamageField);
    }

    // 기본 공격 2 데미지 주기
    public void OnGiveDamageBasicAttack2()
    {
        GiveDamageFieldHashing(_basicAttack2DamageField);
    }

    // 기본 공격 2 바닥 크랙 파티클 생성
    public void OnGroundCrackParticleBasicAttack2()
    {
        Vector3 pos = _basicAttack2GroundCrackTransform.position;
        Quaternion rot = _basicAttack2GroundCrackTransform.rotation;
        Instantiate(_basicAttack2GroundCrackParticle, pos, rot);
        Debug.Log("호출");
    }

    // 기본 공격 3 해시하고 데미지 주기
    public void OnGiveDamageBasicAttack3Hashing()
    {
        GiveDamageFieldHashing(_basicAttack3DamageField);
    }

    // 기본 공격 3 해시하지 않고 데미지 주기
    public void OnGiveDamageBasicAttack3NoHasing()
    {
        GiveDamageFieldNoHashing(_basicAttack3DamageField);
    }

    // 기본 공격 4 해시하고 데미지 주기
    public void OnGiveDamageBasicAttack4Hashing()
    {
        GiveDamageFieldHashing(_basicAttack4DamageField);
    }

    // 기본 공격 4 해시하지 않고 데미지 주기
    public void OnGiveDamageBasicAttack4NoHashing()
    {
        GiveDamageFieldNoHashing(_basicAttack4DamageField);
    }

    // 달리기 공격 1타 데미지 주기
    public void OnGiveDamageRunAttack1HitNoHashing()
    {
        GiveDamageFieldNoHashing(_runAttack1HitDamageField);
    }

    // 달리기 공격 2타 데미지 주기
    public void OnGiveDamageRunAttack2HitNoHashing()
    {
        GiveDamageFieldNoHashing(_runAttack2HitDamageField);
    }

    // 회피 공격 1타 데미지
    public void OnGiveDamageDodgeAttack1Hit()
    {
        GiveDamageFieldNoHashing(_dodgeAttack1HitDamageField);
    }

    // 회피 공격 2타 데미지
    public void OnGiveDamageDodgeAttack2Hit()
    {
        GiveDamageFieldNoHashing(_dodgeAttack2HitDamageField);
    }

    // 회피 공격 막타 데미지
    public void OnGiveDamageDodgeAttackEndHit()
    {
        GiveDamageFieldNoHashing(_dodgeAttackEndDamageField);
    }

    // 회피 공격 막타 범위 데미지
    public void OnGiveDamageDodgeAttackEndRangeHit()
    {
        GiveDamageFieldNoHashing(_dodgeAttackEndRangeDamageField);
    }

    // 데미지를 주는 대상을 해쉬에 추가하면서 공격
    public void GiveDamageFieldHashing(AttackDamageField damageField)
    {
        GiveDamageField(damageField, rememberDamagedTargets: true);
    }

    // 데미지를 주는 대상을 해쉬에 추가하지 않으면서 공격
    public void GiveDamageFieldNoHashing(AttackDamageField damageField)
    {
        GiveDamageField(damageField, rememberDamagedTargets: false);
    }

    private void GiveDamageField(AttackDamageField damageField, bool rememberDamagedTargets)
    {
        if (damageField == null)
        {
            Debug.LogWarning("데미지 필드가 존재하지 않습니다.", this);
            return;
        }

        Collider[] hits = damageField.DetectTargets();
        _targetsInCurrentDetection.Clear();
        _hitStopVictims.Clear();
        bool dealtDamage = false;

        foreach (Collider hit in hits)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !_targetsInCurrentDetection.Add(damageable))
                continue;

            if (rememberDamagedTargets && !_damagedTargets.Add(damageable))
                continue;

            DamageData data = new DamageData(
                gameObject,
                damageField.Damage,
                damageField.HitStopFrame,
                damageField.StaggerLevel);
            if (!damageable.TryTakeDamage(data))
                continue;

            dealtDamage = true;

            // 스킬 게이지 증가
            OnAttackSkillGaugeAdditive?.Invoke(damageField.SkillGaugeAdditive);

            if (damageable is IHitStopParticipant participant)
                _hitStopVictims.Add(participant);
        }

        if (dealtDamage)
            _playerSFXController?.PlayOneShotHitFeedbackSFX();

        if (damageField.HitStopMode == AttackHitStopMode.VictimsOnly)
            HitstopCoordinator.RequestVictimsOnly(_hitStopVictims, damageField.HitStopFrame);
        else
            HitstopCoordinator.Request(
                _ownerHitStopParticipant,
                _hitStopVictims,
                damageField.HitStopFrame);
    }

    // 데미지 입은 대상 해쉬 정리
    public void ClearDamagedTargets()
    {
        _damagedTargets.Clear();
    }
}
