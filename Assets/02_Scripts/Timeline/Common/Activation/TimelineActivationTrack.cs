using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackColor(0.95f, 0.7f, 0.2f)]
[TrackClipType(typeof(TimelineActivationClip))]
[TrackBindingType(typeof(TimelineActivationTarget))]
public sealed class TimelineActivationTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mixer = ScriptPlayable<TimelineActivationMixer>.Create(graph, inputCount);
        mixer.GetBehaviour().Initialize(graph.GetResolver() as PlayableDirector, this);
        return mixer;
    }

    protected override void OnCreateClip(TimelineClip clip)
    {
        clip.displayName = "Active";
        double frameRate = timelineAsset != null ? timelineAsset.editorSettings.frameRate : 60d;
        clip.duration = 2d / (frameRate > 0d ? frameRate : 60d);
        clip.easeInDuration = clip.easeOutDuration = 0d;
    }

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        var target = director.GetGenericBinding(this) as TimelineActivationTarget;
        if (target != null)
            target.GatherProperties(driver);
        base.GatherProperties(director, driver);
    }
}
