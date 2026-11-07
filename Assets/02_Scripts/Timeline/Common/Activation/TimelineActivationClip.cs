using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// The clip's presence is the ON request. Timing belongs to TimelineClip.
public sealed class TimelineActivationClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.None;
    public override double duration => 2d / 60d;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
        ScriptPlayable<TimelineActivationBehaviour>.Create(graph);
}

public sealed class TimelineActivationBehaviour : PlayableBehaviour { }
