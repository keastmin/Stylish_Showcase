using UnityEngine;

public class PlayerSFXController : MonoBehaviour
{
    [SerializeField] private AudioSource _as;

    [SerializeField] private AudioClip[] _basicAttackSlashSFX;
    [SerializeField] private AudioClip _runAttackSlashSFX;
    [SerializeField] private AudioClip _dodgeAttackSlashSFX;
    [SerializeField] private AudioClip[] _dodgeJetSFX;
    [SerializeField] private AudioClip[] _hitFeedbackSFX;
    [SerializeField] private AudioClip _perfectDodgeSFX;
    [SerializeField] private AudioClip[] _skillSlashSFX;
    [SerializeField] private AudioClip _skillImpactFrameSFX;

    public void PlayOneShotBasicSlashSFXRandom()
    {
        int randIndex = Random.Range(0, _basicAttackSlashSFX.Length);
        _as.PlayOneShot(_basicAttackSlashSFX[randIndex]);
    }

    public void PlayOneShotRunAttackSFX()
    {
        _as.PlayOneShot(_runAttackSlashSFX);
    }

    public void PlayOneShotDodgeAttackSlashSFX()
    {
        _as.PlayOneShot(_dodgeAttackSlashSFX);
    }

    public void PlayOneShotDodgeJetSFX()
    {
        int randIndex = Random.Range(0, _dodgeJetSFX.Length);
        _as.PlayOneShot(_dodgeJetSFX[randIndex]);
    }

    public void PlayOneShotHitFeedbackSFX()
    {
        if (_as == null || _hitFeedbackSFX == null || _hitFeedbackSFX.Length == 0)
            return;

        int randIndex = Random.Range(0, _hitFeedbackSFX.Length);
        AudioClip clip = _hitFeedbackSFX[randIndex];
        if (clip != null)
            _as.PlayOneShot(clip);
    }

    public void PlayOneShotPerfectDodgeSFX()
    {
        _as.PlayOneShot(_perfectDodgeSFX);
    }

    public void PlayOneShotSkillSlashSFX()
    {
        if (_as == null || _skillSlashSFX == null || _skillSlashSFX.Length == 0)
            return;

        int randIndex = Random.Range(0, _skillSlashSFX.Length);
        AudioClip clip = _skillSlashSFX[randIndex];
        if (clip != null)
            _as.PlayOneShot(clip);
    }

    public void PlayOneShotSkillImpactFrameSFX()
    {
        _as.PlayOneShot(_skillImpactFrameSFX);
    }
}
