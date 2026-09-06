using UnityEngine;

public class EnemySFX : MonoBehaviour
{
    [SerializeField] private AudioSource _as;
    [SerializeField] private AudioClip _attackNoticeClip;
    [SerializeField] private AudioClip[] _closeAttackSlashClip;
    [SerializeField] private AudioClip _closeAttackHitClip;
    [SerializeField] private AudioClip[] _deadClip;

    public void OnAttackNoticeSFX()
    {
        _as.PlayOneShot(_attackNoticeClip, 3f);
    }

    public void OnCloseAttackSlashSFX()
    {
        int randIndex = Random.Range(0, _closeAttackSlashClip.Length);
        _as.PlayOneShot(_closeAttackSlashClip[randIndex], 1.5f);
    }

    public void OnCloseAttackHitSFX()
    {
        _as.PlayOneShot(_closeAttackHitClip, 2f);
    }

    public void OnDeadSFX()
    {
        int randIndex = Random.Range(0, _deadClip.Length);
        _as.PlayOneShot(_deadClip[randIndex], 1.5f);
    }
}