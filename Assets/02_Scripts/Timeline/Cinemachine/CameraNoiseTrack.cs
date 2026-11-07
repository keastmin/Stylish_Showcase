using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackColor(0.35f, 0.65f, 0.95f)]
[TrackClipType(typeof(CameraNoiseClip))]
[TrackBindingType(typeof(CinemachineCameraNoiseTarget))]
public sealed class CameraNoiseTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mixer = ScriptPlayable<CameraNoiseMixer>.Create(graph, inputCount);
        mixer.GetBehaviour().Initialize(graph.GetResolver() as PlayableDirector, this);
        return mixer;
    }

    protected override void OnCreateClip(TimelineClip clip) => clip.displayName = "Camera Noise";
}
