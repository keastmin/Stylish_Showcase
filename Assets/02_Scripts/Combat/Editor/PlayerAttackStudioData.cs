using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

internal sealed class PlayerAttackStudioContext
{
    public PlayerCore Player;
    public DirectorID Id;
    public string Label;
    public PlayableDirector Director;
    public TimelineAsset Timeline;
    public AnimatorState State;
    public AnimationClip Clip;
    public float Speed = 1f;
    public string AnimationIssue;

    public bool CanEditAnimation => State != null && Clip != null && Timeline != null && string.IsNullOrEmpty(AnimationIssue);
    public double AnimationDuration => Clip != null && Speed > 0f ? Clip.length / (double)Speed : 0d;
    public double Duration => Math.Max(AnimationDuration, Timeline != null ? Timeline.duration : 0d);
}

internal sealed class PlayerAttackStudioSignal
{
    public PlayerAttackStudioContext Context;
    public SignalEmitter Marker;
    public TrackAsset Track;
    public SignalReceiver Receiver;
    public PlayerAttackInstanceContainer Owner;
    public string DamageFieldProperty;
    public AttackDamageField Field;
    public string Label;
    public string Method;
    public bool IsHit;
    public bool IsBound;
    public string BindingIssue;
}

/// <summary>
/// Adapts the ordinary attack FSM's existing Animator states, Timeline signals and
/// receiver fields. Opening the Studio never migrates assets or creates bindings.
/// Perfect-dodge counter phases deliberately have no entry in this catalog.
/// </summary>
internal static class PlayerAttackStudioData
{
    private static readonly DirectorID[] BasicIds =
    {
        DirectorID.BasicAttack1, DirectorID.BasicAttack2,
        DirectorID.BasicAttack3, DirectorID.BasicAttack4
    };

    private static readonly Dictionary<string, string> DamageProperties = new()
    {
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack1), "_basicAttack1DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack2), "_basicAttack2DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack3Hashing), "_basicAttack3DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack3NoHasing), "_basicAttack3DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack4Hashing), "_basicAttack4DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageBasicAttack4NoHashing), "_basicAttack4DamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageRunAttack1HitNoHashing), "_runAttack1HitDamageField" },
        { nameof(PlayerAttackInstanceContainer.OnGiveDamageRunAttack2HitNoHashing), "_runAttack2HitDamageField" }
    };

    public static DirectorID[] GetSupportedIds(bool ordinaryDodge = false) =>
        ordinaryDodge ? new[] { DirectorID.RunAttack } : (DirectorID[])BasicIds.Clone();

    public static List<PlayerAttackStudioContext> GetActions(PlayerCore player, bool ordinaryDodge) =>
        GetSupportedIds(ordinaryDodge).Select(id => Resolve(player, id)).ToList();

    public static PlayerAttackStudioContext Resolve(PlayerCore player, DirectorID id)
    {
        EnsureSupported(id);
        var context = new PlayerAttackStudioContext
        {
            Player = player, Id = id,
            Label = id == DirectorID.RunAttack ? "일반 회피 공격 · 달리기 공격" : $"기본 공격 {Array.IndexOf(BasicIds, id) + 1}"
        };
        if (player == null)
        {
            context.AnimationIssue = "플레이어를 선택하세요.";
            return context;
        }

        // The runtime dictionary is only initialized by Awake. Read its authored
        // source in Edit Mode instead of calling runtime initialization methods.
        var container = player.GetComponent<TimelineDirectorContainer>();
        if (container != null)
        {
            var serialized = new SerializedObject(container);
            var infos = serialized.FindProperty("_directorInfos");
            if (infos != null && infos.isArray)
                for (int index = 0; index < infos.arraySize; index++)
                {
                    var info = infos.GetArrayElementAtIndex(index);
                    if (info.FindPropertyRelative("ID").intValue != (int)id) continue;
                    context.Director = info.FindPropertyRelative("Director").objectReferenceValue as PlayableDirector;
                    break;
                }
        }
        context.Timeline = context.Director != null ? context.Director.playableAsset as TimelineAsset : null;

        var animator = player.Animator;
        var runtimeController = animator != null ? animator.runtimeAnimatorController : null;
        if (runtimeController is AnimatorOverrideController)
            context.AnimationIssue = "Animator Override Controller는 이 툴에서 모션을 교체할 수 없습니다. 원본 컨트롤러와 오버라이드를 먼저 확인하세요.";
        else if (runtimeController is not AnimatorController controller)
            context.AnimationIssue = "플레이어 Animator에 Animator Controller가 연결되어 있지 않습니다.";
        else
        {
            string stateName = id == DirectorID.RunAttack ? "Run Attack" : $"Basic Attack {Array.IndexOf(BasicIds, id) + 1}";
            // These FSMs explicitly read layer zero. Do not accidentally edit a
            // similarly named state in an additive or synchronized layer.
            var states = controller.layers.Length > 0
                ? FindStates(controller.layers[0].stateMachine, stateName).ToArray()
                : Array.Empty<AnimatorState>();
            if (states.Length != 1)
                context.AnimationIssue = $"Animator 기본 레이어에서 '{stateName}' 상태를 하나로 확인할 수 없습니다.";
            else
            {
                context.State = states[0];
                context.Clip = context.State.motion as AnimationClip;
                context.Speed = context.State.speed;
                context.AnimationIssue = GetAnimationIssue(context);
            }
        }
        return context;
    }

    public static List<PlayerAttackStudioSignal> GetSignals(PlayerAttackStudioContext context)
    {
        if (context == null) return new List<PlayerAttackStudioSignal>();
        EnsureSupported(context.Id);
        if (context.Timeline == null) return new List<PlayerAttackStudioSignal>();
        return EnumerateTracks(context.Timeline)
            .SelectMany(track => track.GetMarkers().OfType<SignalEmitter>().Select(marker => ReadSignal(context, track, marker)))
            .OrderBy(row => row.Marker.time).ToList();
    }

    public static void SetMarkerTime(PlayerAttackStudioSignal row, double time)
    {
        ValidateSignal(row);
        time = ValidateTime(time);
        if (Math.Abs(row.Marker.time - time) < 0.0000001d) return;
        Undo.RegisterCompleteObjectUndo(row.Marker, "Move player attack signal");
        row.Marker.time = time;
        MarkSignalDirty(row);
    }

    public static void SetSignalTime(PlayerAttackStudioSignal row, double time) => SetMarkerTime(row, time);

    /// <summary>The duplicate keeps the SignalAsset and hence the same receiver reaction and shared damage field.</summary>
    public static PlayerAttackStudioSignal CloneSignal(PlayerAttackStudioSignal row, double time)
    {
        ValidateSignal(row);
        time = ValidateTime(time);
        Undo.RegisterCompleteObjectUndo(row.Track, "Duplicate player attack signal");
        // Timeline's CreateMarker registers creation Undo and attaches the new
        // subasset. Never clone or replace the shared SignalAsset / UnityEvent.
        var clone = row.Track.CreateMarker<SignalEmitter>(time);
        clone.name = row.Marker.name;
        clone.asset = row.Marker.asset;
        clone.retroactive = row.Marker.retroactive;
        clone.emitOnce = row.Marker.emitOnce;
        var result = ReadSignal(row.Context, row.Track, clone);
        MarkSignalDirty(result);
        return result;
    }

    public static void RemoveSignal(PlayerAttackStudioSignal row)
    {
        ValidateSignal(row);
        var track = row.Track;
        Undo.RegisterCompleteObjectUndo(track, "Remove player attack signal");
        // DeleteMarker already records destruction Undo. Keep the receiver's
        // shared reaction because other markers/directors can still reference it.
        track.DeleteMarker(row.Marker);
        EditorUtility.SetDirty(track);
        if (track.timelineAsset != null) EditorUtility.SetDirty(track.timelineAsset);
    }

    public static void SetAnimation(PlayerAttackStudioContext context, AnimationClip clip, float speed)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        EnsureSupported(context.Id);
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < TimelineClip.kTimeScaleMin || speed > TimelineClip.kTimeScaleMax)
            throw new ArgumentOutOfRangeException(nameof(speed), "모션 배속은 Timeline이 지원하는 0.001–1000 범위여야 합니다.");
        string issue = GetAnimationIssue(context);
        if (!string.IsNullOrEmpty(issue)) throw new InvalidOperationException(issue);
        var reference = FindAnimationReferences(context).Single();
        var animation = (AnimationPlayableAsset)reference.clip.asset;

        Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]
        {
            context.State, reference.track, animation
        }, "Change player attack animation");
        context.State.motion = clip;
        context.State.speed = speed;
        // speedParameterActive/timeParameterActive are validated, never disabled
        // or rewritten. The existing FSM retains all transitions and triggers.
        animation.clip = clip;
        reference.clip.start = 0d;
        reference.clip.clipIn = 0d;
        reference.clip.timeScale = speed;
        reference.clip.duration = Math.Max(double.Epsilon, clip.length / (double)speed);
        reference.clip.displayName = clip.name;
        EditorUtility.SetDirty(context.State);
        EditorUtility.SetDirty(reference.track);
        EditorUtility.SetDirty(animation);
        EditorUtility.SetDirty(context.Timeline);
        context.Clip = clip;
        context.Speed = speed;
        context.AnimationIssue = null;
    }

    public static IEnumerable<TrackAsset> EnumerateTracks(TimelineAsset timeline)
    {
        if (timeline == null) yield break;
        if (timeline.markerTrack != null) yield return timeline.markerTrack;
        foreach (var root in timeline.GetRootTracks())
            foreach (var track in EnumerateTrack(root)) yield return track;
    }

    private static IEnumerable<TrackAsset> EnumerateTrack(TrackAsset track)
    {
        yield return track;
        foreach (var child in track.GetChildTracks())
            foreach (var descendant in EnumerateTrack(child)) yield return descendant;
    }

    private static IEnumerable<AnimatorState> FindStates(AnimatorStateMachine machine, string stateName)
    {
        if (machine == null) yield break;
        foreach (var child in machine.states)
            if (child.state != null && child.state.name == stateName) yield return child.state;
        foreach (var child in machine.stateMachines)
            foreach (var state in FindStates(child.stateMachine, stateName)) yield return state;
    }

    private static string GetAnimationIssue(PlayerAttackStudioContext context)
    {
        if (context.Player != null && context.Player.Animator != null &&
            context.Player.Animator.runtimeAnimatorController is AnimatorOverrideController)
            return "Animator Override Controller의 모션 교체는 지원하지 않습니다.";
        if (context.State == null) return "연결된 Animator 상태가 없습니다.";
        if (context.State.motion is not AnimationClip) return "Blend Tree 또는 비어 있는 모션은 이 툴에서 교체할 수 없습니다.";
        if (context.State.speedParameterActive)
            return $"Animator 배속이 '{context.State.speedParameter}' 파라미터에 연결되어 있어 고정 배속으로 편집할 수 없습니다.";
        if (context.State.timeParameterActive || context.State.cycleOffsetParameterActive)
            return "Animator 재생 시각 또는 시작 오프셋 파라미터가 활성화되어 있어 모션 편집을 지원하지 않습니다.";
        if (context.State.speed <= 0f || float.IsNaN(context.State.speed) || float.IsInfinity(context.State.speed))
            return "Animator 상태의 배속이 양수가 아닙니다. 컨트롤러에서 먼저 확인하세요.";
        if (context.Timeline == null) return "연결된 Timeline이 없습니다.";
        var references = FindAnimationReferences(context).ToArray();
        if (references.Length != 1)
            return "플레이어의 음소거된 참조 애니메이션 클립을 하나로 확인할 수 없습니다. Unity Timeline 연결을 확인하세요.";
        if (Math.Abs(references[0].clip.start) > 0.00001d || Math.Abs(references[0].clip.clipIn) > 0.00001d)
            return "참조 애니메이션의 시작 또는 Clip In이 0이 아닙니다. Animator와 같은 시작점으로 먼저 맞추세요.";
        return null;
    }

    private static IEnumerable<(AnimationTrack track, TimelineClip clip)> FindAnimationReferences(PlayerAttackStudioContext context)
    {
        foreach (var track in EnumerateTracks(context.Timeline).OfType<AnimationTrack>())
        {
            if (!track.mutedInHierarchy) continue;
            if (context.Player != null)
            {
                var binding = context.Director != null ? context.Director.GetGenericBinding(track) : null;
                var boundAnimator = binding as Animator ?? (binding as GameObject)?.GetComponent<Animator>();
                if (boundAnimator != context.Player.Animator) continue;
            }
            foreach (var clip in track.GetClips())
                if (clip.asset is AnimationPlayableAsset animation && animation.clip == context.State?.motion)
                    yield return (track, clip);
        }
    }

    private static PlayerAttackStudioSignal ReadSignal(PlayerAttackStudioContext context, TrackAsset track, SignalEmitter marker)
    {
        var row = new PlayerAttackStudioSignal
        {
            Context = context, Track = track, Marker = marker,
            Label = marker.asset != null ? marker.asset.name : "연결되지 않은 신호"
        };
        if (context?.Director == null || marker.asset == null) return row;
        var binding = context.Director.GetGenericBinding(track);
        row.Receiver = binding as SignalReceiver ?? (binding as GameObject)?.GetComponent<SignalReceiver>();
        if (row.Receiver == null && track == context.Timeline.markerTrack)
            row.Receiver = context.Director.GetComponent<SignalReceiver>();
        var reaction = row.Receiver != null ? row.Receiver.GetReaction(marker.asset) : null;
        if (reaction == null) return row;

        var methods = new List<string>();
        for (int index = 0; index < reaction.GetPersistentEventCount(); index++)
        {
            if (reaction.GetPersistentListenerState(index) == UnityEventCallState.Off) continue;
            var target = reaction.GetPersistentTarget(index);
            string method = reaction.GetPersistentMethodName(index);
            if (target == null || string.IsNullOrEmpty(method)) continue;
            row.IsBound = true;
            methods.Add(method);
            if (target is not PlayerAttackInstanceContainer owner || !DamageProperties.TryGetValue(method, out string property)) continue;
            if (row.Owner != null && (row.Owner != owner || row.DamageFieldProperty != property))
            {
                row.BindingIssue = "하나의 신호에 여러 피해 필드가 연결되어 있습니다. Signal Receiver에서 각 연결을 확인하세요.";
                continue;
            }
            row.Owner = owner;
            row.DamageFieldProperty = property;
            row.Field = typeof(PlayerAttackInstanceContainer).GetField(property, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as AttackDamageField;
            row.IsHit = true;
            row.Method = method;
            row.Label = row.Field != null ? row.Field.Name : method;
        }
        if (string.IsNullOrEmpty(row.Method)) row.Method = string.Join(", ", methods);
        return row;
    }

    private static void EnsureSupported(DirectorID id)
    {
        if (id != DirectorID.RunAttack && !BasicIds.Contains(id))
            throw new ArgumentOutOfRangeException(nameof(id), "기본 공격 1–4와 일반 회피/달리기 공격만 지원합니다.");
    }

    private static void ValidateSignal(PlayerAttackStudioSignal row)
    {
        if (row == null || row.Marker == null || row.Track == null || row.Marker.parent != row.Track)
            throw new InvalidOperationException("편집할 신호가 Timeline에 연결되어 있지 않습니다.");
        if (row.Context != null) EnsureSupported(row.Context.Id);
    }

    private static double ValidateTime(double time)
    {
        if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
        return Math.Max(0d, time);
    }

    private static void MarkSignalDirty(PlayerAttackStudioSignal row)
    {
        EditorUtility.SetDirty(row.Marker);
        EditorUtility.SetDirty(row.Track);
        if (row.Track.timelineAsset != null) EditorUtility.SetDirty(row.Track.timelineAsset);
    }
}
