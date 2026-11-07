using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public sealed class TimelineActivationMixer : PlayableBehaviour
{
    private PlayableDirector _director;
    private TrackAsset _track;
    private TimelineActivationTarget _binding;
    private TimelineActivationLifetime _lifetime;

    public void Initialize(PlayableDirector director, TrackAsset track)
    {
        _director = director;
        _track = track;
        if (_director == null)
            return;
        _director.stopped += OnDirectorStopped;
        _lifetime = _director.gameObject.AddComponent<TimelineActivationLifetime>();
        _lifetime.hideFlags = HideFlags.HideAndDontSave;
        _lifetime.Director = _director;
        _lifetime.Release = ReleaseRequest;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        // Director bindings can change before its cached output userData updates.
        var target = _director != null && _track != null
            ? _director.GetGenericBinding(_track) as TimelineActivationTarget : null;
        if (_binding != target)
        {
            ReleaseRequest();
            _binding = target;
        }
        if (_binding == null)
            return;
        if (_director == null || !_director.isActiveAndEnabled)
        {
            ReleaseRequest();
            return;
        }

        bool active = false;
        for (int i = 0; i < playable.GetInputCount(); i++)
            active |= playable.GetInputWeight(i) > 0f;
        _binding.SetRequestedActive(this, active);
    }

    private void OnDirectorStopped(PlayableDirector director) => ReleaseRequest();

    public override void OnGraphStop(Playable playable)
    {
        // Pause and zero graph speed hold the request. Disabled owners release it.
        if (_director == null || !_director.isActiveAndEnabled)
            ReleaseRequest();
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_director != null)
            _director.stopped -= OnDirectorStopped;
        ReleaseRequest();
        if (_lifetime != null)
        {
            _lifetime.Release = null;
            if (Application.isPlaying)
                Object.Destroy(_lifetime);
            else
                Object.DestroyImmediate(_lifetime);
        }
    }

    private void ReleaseRequest()
    {
        if (_binding != null)
            _binding.ReleaseOwner(this);
    }
}
