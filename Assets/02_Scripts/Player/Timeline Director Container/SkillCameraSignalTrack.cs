using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackBindingType(typeof(SignalReceiver)), TrackColor(0.45f, 0.55f, 0.85f)]
public sealed class SkillCameraSignalTrack : SignalTrack
{
    public override bool CanCreateTrackMixer() => true;
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var playable = ScriptPlayable<SkillCameraSignalMixer>.Create(graph, inputCount);
        playable.GetBehaviour().Initialize(graph.GetResolver() as PlayableDirector, this);
        return playable;
    }
    public double LatestSignalTime(SignalAsset signal, double time)
    {
        double result = double.NaN;
        if (signal == null || mutedInHierarchy)
            return result;
        foreach (IMarker marker in GetMarkers())
            if (marker is SignalEmitter emitter && emitter.asset == signal && emitter.time <= time &&
                (double.IsNaN(result) || emitter.time > result))
                result = emitter.time;
        return result;
    }
    public override void GatherProperties(PlayableDirector director, IPropertyCollector collector)
    {
        if (director.GetGenericBinding(this) is SignalReceiver receiver)
            receiver.GetComponent<PlayerSkillCameraDirector>()?.GatherPreviewProperties(collector);
        base.GatherProperties(director, collector);
    }
}

public sealed class SkillCameraSignalMixer : PlayableBehaviour
{
    private PlayableDirector _director;
    private SkillCameraSignalTrack _track;
    private PlayerSkillCameraDirector _controller;
    private int _sessionVersion;
    private TimelineActivationLifetime _lifetime;
    public void Initialize(PlayableDirector director, SkillCameraSignalTrack track)
    {
        _director = director;
        _track = track;
        if (_director == null)
            return;
        _director.stopped += OnStopped;
        _lifetime = _director.gameObject.AddComponent<TimelineActivationLifetime>();
        _lifetime.hideFlags = HideFlags.HideAndDontSave;
        _lifetime.Director = director;
        _lifetime.Release = Release;
    }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var receiver = _director != null ? _director.GetGenericBinding(_track) as SignalReceiver : null;
        var controller = receiver != null ? receiver.GetComponent<PlayerSkillCameraDirector>() : null;
        if (_controller != controller)
        {
            Release();
            _controller = controller;
        }
        if (_controller != null)
        {
            _sessionVersion = _controller.SessionVersion;
            _controller.EvaluateTimeline(_track, _director.time);
        }
    }
    private void OnStopped(PlayableDirector director) => Release();
    private void Release()
    {
        // Destroying the previous graph must not discard a newly captured skill pose.
        if (_controller != null && _controller.SessionVersion == _sessionVersion)
            _controller.EndTimelineSession();
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
