using UnityEngine;

public class ProjectileSFX : MonoBehaviour
{
    [SerializeField] private AudioSource _as;
    [SerializeField] private AudioClip[] _projectileSlashClip;
    [SerializeField, Min(1)] private int _soundOnCount = 5;
    [SerializeField, Min(0.01f)] private float _endTime = 0.6f;

    private float _soundOnSpaceTime;
    private float _nextTargetTime;
    private float _currentTime;
    private int _currentSoundOnCount;

    private void OnEnable()
    {
        _soundOnSpaceTime = Mathf.Max(0.01f, _endTime) / Mathf.Max(1, _soundOnCount);
        _currentTime = 0f;
        _currentSoundOnCount = 0;

        // 생성 다음 프레임의 Update까지 기다리지 않고 활성화된 프레임에 첫 소리를 재생합니다.
        PlayNextProjectileSlashSFX();
        _nextTargetTime = _soundOnSpaceTime;
    }

    private void Update()
    {
        _currentTime += Time.deltaTime;

        if(_currentTime >= _nextTargetTime && _currentSoundOnCount < _soundOnCount)
        {
            PlayNextProjectileSlashSFX();
            _nextTargetTime += _soundOnSpaceTime;
        }
    }

    private void PlayNextProjectileSlashSFX()
    {
        PlayOneShotProjectileSlashSFXRandom();
        _currentSoundOnCount++;
    }

    public void PlayOneShotProjectileSlashSFXRandom()
    {
        if (_as == null || _projectileSlashClip == null || _projectileSlashClip.Length == 0)
            return;

        int randIndx = Random.Range(0, _projectileSlashClip.Length);
        AudioClip clip = _projectileSlashClip[randIndx];
        if (clip != null)
            _as.PlayOneShot(clip);
    }
}
