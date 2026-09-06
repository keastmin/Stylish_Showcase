using System;
using UnityEngine;
using UnityEngine.Events;

public class PlayerAnimationEventSender : MonoBehaviour
{
    private Animator _animator;

    public UnityEvent OnLeftBoostEffect; // 왼쪽 발 부스터 이펙드
    public UnityEvent OnRightBoostEffect; // 오른쪽 발 부스터 이펙트
    public UnityEvent OnBothBoostEffect; // 양쪽 발 부스터 이펙트
    public UnityEvent OnDashWindEffect; // 대쉬 바람 이펙트
    public UnityEvent OnEnableOtherBehaviour; // 다른 행동 가능
    public UnityEvent OnAnimationEnd; // 애니메이션 종료
    public UnityEvent OnHighSpeedRotationSpeedEnd; // 회전이 빨라지는 구간 종료
    public UnityEvent OnPerfectDodgeEnd; // 완벽 회피 종료
    public UnityEvent OnDodgeSFX; // 회피 SFX 재생

    private void Awake()
    {
        TryGetComponent(out _animator);
    }

    public void OnLeftBoostEffectInvoek()
    {
        OnLeftBoostEffect?.Invoke();
    }

    public void OnRightBoostEffectInvoke()
    {
        OnRightBoostEffect?.Invoke();
    }

    public void OnBothBoostEffectInvoke()
    {
        OnBothBoostEffect?.Invoke();
    }

    public void OnDashWindEffectInvoke()
    {
        OnDashWindEffect?.Invoke();
    }

    public void OnEnableOtherBehaviourInvoke()
    {
        // 블랜딩 중이면 즉시 종료
        if (_animator.IsInTransition(0))
            return;
        OnEnableOtherBehaviour?.Invoke();
    }

    public void OnAnimationEndInvoke()
    {
        // 블랜딩 중이면 즉시 종료
        if (_animator.IsInTransition(0))
            return;
        OnAnimationEnd?.Invoke();
    }

    public void OnHighSpeedRotationSpeedEndInvoke()
    {
        OnHighSpeedRotationSpeedEnd?.Invoke();
    }

    public void OnPerfectDodgeEndInvoke()
    {
        OnPerfectDodgeEnd?.Invoke();
    }

    public void OnDodgeJetSFXInvoke()
    {
        OnDodgeSFX?.Invoke();
    }
}