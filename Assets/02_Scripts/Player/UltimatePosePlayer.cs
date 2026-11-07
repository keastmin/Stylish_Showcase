using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Shared deterministic pose sampler. The caller owns world translation and facing.</summary>
public sealed class UltimatePosePlayer : IDisposable
{
    private Animator _animator;
    private PlayerUltimateDefinition _definition;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private AnimationClipPlayable[] _clips;
    private bool _previousApplyRootMotion;
    private RuntimeAnimatorController _previousController;

    public static UltimatePosePlayer Create(Animator animator, PlayerUltimateDefinition definition)
    {
        if (animator == null) throw new ArgumentNullException(nameof(animator));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        var player = new UltimatePosePlayer();
        try
        {
            player.Initialize(animator, definition);
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    private void Initialize(Animator animator, PlayerUltimateDefinition definition)
    {
        _animator = animator;
        _definition = definition;
        _previousApplyRootMotion = animator.applyRootMotion;
        _previousController = animator.runtimeAnimatorController;
        Vector3 localPosition = animator.transform.localPosition;
        Quaternion localRotation = animator.transform.localRotation;
        // Manual evaluation happens in Update. The default controller must not evaluate
        // again later in the frame and replace the sampled attack pose with locomotion.
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        _graph = PlayableGraph.Create("Ultimate pose sequence");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        int count = definition.HasSequence ? definition.sequence.Count : 1;
        _clips = new AnimationClipPlayable[count];
        _mixer = AnimationMixerPlayable.Create(_graph, count);
        for (int i = 0; i < count; i++)
        {
            AnimationClip clip = definition.HasSequence ? definition.sequence[i]?.clip : definition.clip;
            if (clip == null) continue;
            _clips[i] = AnimationClipPlayable.Create(_graph, clip);
            _clips[i].SetApplyFootIK(false);
            _clips[i].SetApplyPlayableIK(false);
            _graph.Connect(_clips[i], 0, _mixer, i);
        }
        var output = AnimationPlayableOutput.Create(_graph, "Ultimate pose", animator);
        output.SetSourcePlayable(_mixer);
        _graph.Play();
    }

    public void Sample(float sequenceTime)
    {
        if (!_graph.IsValid() || _animator == null) return;
        for (int i = 0; i < _clips.Length; i++) _mixer.SetInputWeight(i, 0f);
        int index = 0;
        float elapsed = sequenceTime;
        if (_definition.HasSequence && !UltimateSequenceUtility.GetSegment(_definition, sequenceTime, out index, out elapsed)) return;
        if (!_clips[index].IsValid()) return;
        UltimateSequenceClip entry = _definition.HasSequence ? _definition.sequence[index] : null;
        AnimationClip clip = entry?.clip ?? _definition.clip;
        float sourceTime = entry != null ? entry.SourceTime(elapsed) : sequenceTime;
        _clips[index].SetTime(Mathf.Clamp(sourceTime, 0f, Mathf.Max(0f, clip.length - 0.00001f)));
        float blend = entry != null && index > 0 && entry.poseBlendIn > 0f ? Mathf.Clamp01(elapsed / entry.poseBlendIn) : 1f;
        _mixer.SetInputWeight(index, blend);
        if (blend < 1f && _clips[index - 1].IsValid())
        {
            UltimateSequenceClip previous = _definition.sequence[index - 1];
            _clips[index - 1].SetTime(Mathf.Min(previous.SourceEnd, Mathf.Max(0f, previous.clip.length - 0.00001f)));
            _mixer.SetInputWeight(index - 1, 1f - blend);
        }
        Vector3 localPosition = _animator.transform.localPosition;
        Quaternion localRotation = _animator.transform.localRotation;
        _graph.Evaluate(0f);
        _animator.transform.SetLocalPositionAndRotation(localPosition, localRotation);
    }

    public void Dispose()
    {
        try
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
        finally
        {
            if (_animator != null)
            {
                Vector3 localPosition = _animator.transform.localPosition;
                Quaternion localRotation = _animator.transform.localRotation;
                // The FSM selects Idle/Hit/etc. after releasing this player. Restoring
                // the controller first allows that state's animation trigger to work.
                _animator.runtimeAnimatorController = _previousController;
                _animator.applyRootMotion = _previousApplyRootMotion;
                _animator.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            }
            _animator = null;
            _definition = null;
            _previousController = null;
            _clips = null;
        }
    }
}
