using UnityEngine;
using UnityEngine.Playables;

public sealed class CameraNoiseMixer : PlayableBehaviour
{
    private PlayableDirector _director;
    private CameraNoiseTrack _track;
    private CinemachineCameraNoiseTarget _target;
    private TimelineActivationLifetime _lifetime;

    public void Initialize(PlayableDirector director, CameraNoiseTrack track)
    {
        _director = director;
        _track = track;
        if (_director == null)
            return;
        _director.stopped += OnStopped;
        _lifetime = _director.gameObject.AddComponent<TimelineActivationLifetime>();
        _lifetime.hideFlags = HideFlags.HideAndDontSave;
        _lifetime.Director = _director;
        _lifetime.Release = Release;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var target = _director != null ? _director.GetGenericBinding(_track) as CinemachineCameraNoiseTarget : null;
        if (_target != target)
        {
            Release();
            _target = target;
        }
        if (_director == null || !_director.isActiveAndEnabled || _target == null)
        {
            Release();
            return;
        }
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            float weight = playable.GetInputWeight(i);
            if (weight <= 0f)
                continue;
            var input = (ScriptPlayable<CameraNoiseBehaviour>)playable.GetInput(i);
            var settings = input.GetBehaviour().Settings;
            if (settings != null)
                offset += settings.Sample(input.GetTime(), input.GetDuration()) * weight;
        }
        _target.SetOffset(this, offset);
    }

    private void OnStopped(PlayableDirector director) => Release();
    private void Release()
    {
        if (_target != null)
            _target.Release(this);
    }
    public override void OnGraphStop(Playable playable)
    {
        if (_director == null || !_director.isActiveAndEnabled)
            Release();
    }
    public override void OnPlayableDestroy(Playable playable)
    {
        if (_director != null)
            _director.stopped -= OnStopped;
        Release();
        if (_lifetime == null)
            return;
        _lifetime.Release = null;
        if (Application.isPlaying)
            Object.Destroy(_lifetime);
        else
            Object.DestroyImmediate(_lifetime);
    }
}
