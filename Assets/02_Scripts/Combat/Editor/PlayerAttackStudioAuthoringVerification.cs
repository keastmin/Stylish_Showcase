using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Runs native signal and Animator authoring operations on disposable fixtures.</summary>
public static class PlayerAttackStudioAuthoringVerification
{
    private const string ReportPath = "Temp/PlayerAttackStudioAuthoringVerification.result";
    private static readonly List<string> Evidence = new();
    public static string LastReport { get; private set; }

    [MenuItem("Tools/Character/Player/Validate Attack Studios Authoring")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Attack Studio authoring validation requires Edit Mode.");
        Evidence.Clear();
        Directory.CreateDirectory("Temp");
        string snapshot = CaptureAuthoredSnapshot();
        try
        {
            ValidateCatalog();
            ValidateSignals();
            ValidateAnimation();
            Check(snapshot == CaptureAuthoredSnapshot(),
                "All player Timeline, data, controller, prefab and scene files remain unchanged, including perfect dodge, E and Q");
            LastReport = $"PASS: Attack Studios authoring ({Evidence.Count} checks).";
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            Debug.Log(LastReport);
        }
        catch (Exception exception)
        {
            LastReport = "FAIL: Attack Studios authoring: " + exception;
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            throw;
        }
    }

    private static void ValidateCatalog()
    {
        Check(PlayerAttackStudioData.GetSupportedIds().SequenceEqual(new[] {
            DirectorID.BasicAttack1, DirectorID.BasicAttack2, DirectorID.BasicAttack3, DirectorID.BasicAttack4 }),
            "Basic Attack Studio contains exactly the four combo actions");
        Check(PlayerAttackStudioData.GetSupportedIds(true).SequenceEqual(new[] { DirectorID.RunAttack }),
            "Ordinary Dodge Attack Studio contains RunAttack and excludes perfect counter phases");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/08_Prefabs/Combat/Player.prefab");
        Check(prefab != null, "Authored player prefab is available for read-only binding inspection");
        var player = prefab.GetComponent<PlayerCore>();
        var actions = PlayerAttackStudioData.GetActions(player, false)
            .Concat(PlayerAttackStudioData.GetActions(player, true)).ToArray();
        Check(actions.Length == 5, "Five actions resolve from serialized prefab Director bindings");
        int[] expected = { 1, 1, 2, 3, 2 };
        for (int i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            Check(action.Director != null && action.Timeline != null && action.State != null && action.Clip != null,
                action.Id + " resolves its real Timeline and Animator motion");
            var hits = PlayerAttackStudioData.GetSignals(action).Where(row => row.IsHit).ToArray();
            Check(hits.Length == expected[i] && hits.All(row => row.IsBound && row.Owner != null && row.Field != null),
                action.Id + " preserves the authored " + expected[i] + " damage callbacks");
            Check(hits.All(row => !row.DamageFieldProperty.StartsWith("_dodgeAttack", StringComparison.Ordinal)),
                action.Id + " never resolves a perfect-counter damage field");
        }
    }

    private static void ValidateSignals()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var hitAsset = ScriptableObject.CreateInstance<SignalAsset>();
        var laterAsset = ScriptableObject.CreateInstance<SignalAsset>();
        try
        {
            var host = new GameObject("Attack Studio signal verification");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            var owner = host.AddComponent<PlayerAttackInstanceContainer>();
            var receiver = host.AddComponent<SignalReceiver>();
            var director = host.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            var track = timeline.CreateTrack<SignalTrack>();
            director.SetGenericBinding(track, receiver);
            var reaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(reaction, owner.OnGiveDamageBasicAttack1);
            receiver.AddReaction(hitAsset, reaction);
            var laterReaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(laterReaction, owner.OnGiveDamageBasicAttack2);
            receiver.AddReaction(laterAsset, laterReaction);
            var early = track.CreateMarker<SignalEmitter>(.2d);
            early.asset = hitAsset;
            early.emitOnce = true;
            early.retroactive = false;
            var late = track.CreateMarker<SignalEmitter>(.8d);
            late.asset = laterAsset;
            var context = new PlayerAttackStudioContext { Director = director, Timeline = timeline, Id = DirectorID.BasicAttack1 };
            var row = PlayerAttackStudioData.GetSignals(context).Single(value => value.Marker == early);
            Check(row.IsHit && row.Owner == owner && row.Method == nameof(owner.OnGiveDamageBasicAttack1) &&
                row.DamageFieldProperty == "_basicAttack1DamageField", "Hit identity comes from the receiver's persistent callback");

            VerifyUndo(() => PlayerAttackStudioData.SetMarkerTime(row, 1.1d),
                () => {
                    Near(early.time, 1.1d, "Moving a signal crosses other hits without changing its identity");
                    Check(early.asset == hitAsset && laterAsset == late.asset && Callback(receiver, hitAsset) == nameof(owner.OnGiveDamageBasicAttack1),
                        "Crossing hit times preserves both SignalAsset and callback binding");
                },
                () => Near(early.time, .2d, "One Undo restores the signal timestamp"));

            VerifyUndo(() => PlayerAttackStudioData.CloneSignal(row, .45d),
                () => {
                    var rows = PlayerAttackStudioData.GetSignals(context);
                    Check(rows.Count == 3, "Clone adds exactly one native signal marker");
                    var clone = rows.Single(value => Math.Abs(value.Marker.time - .45d) < .00001d);
                    Check(clone.Marker != early && clone.Marker.asset == hitAsset && clone.Owner == owner &&
                        clone.DamageFieldProperty == row.DamageFieldProperty && clone.Method == row.Method,
                        "Clone shares its original callback and damage field explicitly");
                    Check(clone.Marker.emitOnce == early.emitOnce && clone.Marker.retroactive == early.retroactive,
                        "Clone preserves native notification flags");
                    Check(receiver.GetRegisteredSignals().Count() == 2,
                        "Cloning a hit does not alter the receiver's callback table");
                },
                () => Check(track.GetMarkers().Count() == 2, "One Undo removes only the cloned marker"));

            VerifyUndo(() => PlayerAttackStudioData.RemoveSignal(row),
                () => {
                    Check(track.GetMarkers().Count() == 1 && track.GetMarkers().OfType<SignalEmitter>().Single() == late,
                        "Removal deletes only the chosen signal");
                    Check(Callback(receiver, hitAsset) == nameof(owner.OnGiveDamageBasicAttack1),
                        "Removal leaves shared receiver callbacks available to other markers");
                },
                () => Check(track.GetMarkers().OfType<SignalEmitter>().Any(marker => marker.asset == hitAsset && Math.Abs(marker.time - .2d) < .00001d),
                    "One Undo restores the removed signal and binding"));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            DestroyTimeline(timeline);
            Object.DestroyImmediate(hitAsset);
            Object.DestroyImmediate(laterAsset);
        }
    }

    private static void ValidateAnimation()
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var before = CreateAnimation(2);
        var after = CreateAnimation(3);
        var state = new AnimatorState { name = "Basic Attack 1", motion = before, speed = 1 };
        try
        {
            var track = timeline.CreateTrack<AnimationTrack>();
            track.muted = true;
            var reference = track.CreateClip<AnimationPlayableAsset>();
            var playable = (AnimationPlayableAsset)reference.asset;
            playable.clip = before;
            reference.start = 0;
            reference.clipIn = 0;
            reference.duration = 2;
            reference.timeScale = 1;
            var context = new PlayerAttackStudioContext { Timeline = timeline, State = state, Clip = before,
                Speed = 1, Id = DirectorID.BasicAttack1 };
            VerifyUndo(() => PlayerAttackStudioData.SetAnimation(context, after, 1.5f),
                () => {
                    var currentReference = track.GetClips().Single();
                    Check(state.motion == after && playable.clip == after,
                        "Changing source updates the real Animator state and muted Timeline reference together");
                    Near(state.speed, 1.5d, "Animator state uses the edited speed");
                    Near(currentReference.timeScale, 1.5d, "Timeline reference uses the same speed");
                    Near(currentReference.duration, 2d, "Reference duration accounts for new source length and speed");
                    Check(track.muted && currentReference.start == 0 && currentReference.clipIn == 0,
                        "Source changes keep Timeline animation muted and preserve its zero-based alignment");
                },
                () => {
                    var currentReference = track.GetClips().Single();
                    Check(state.motion == before && playable.clip == before, "One Undo restores both animation references");
                    Near(state.speed, 1, "One Undo restores Animator speed");
                    Near(currentReference.timeScale, 1, "One Undo restores Timeline speed");
                    Near(currentReference.duration, 2, "One Undo restores reference duration");
                });

            state.speedParameterActive = true;
            state.speedParameter = "AuthoredSpeed";
            bool rejected = false;
            try { PlayerAttackStudioData.SetAnimation(context, after, 2); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && state.motion == before && playable.clip == before && state.speedParameterActive && state.speedParameter == "AuthoredSpeed",
                "Unsupported parameter-driven motion is rejected without rewriting authored parameter settings");
        }
        finally
        {
            Undo.ClearUndo(state);
            Object.DestroyImmediate(state);
            DestroyTimeline(timeline);
            Object.DestroyImmediate(before);
            Object.DestroyImmediate(after);
        }
    }

    private static string Callback(SignalReceiver receiver, SignalAsset asset)
        => receiver.GetReaction(asset).GetPersistentMethodName(0);

    private static AnimationClip CreateAnimation(float length)
    {
        var animation = new AnimationClip();
        AnimationUtility.SetEditorCurve(animation,
            EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), "m_LocalPosition.x"),
            AnimationCurve.Linear(0, 0, length, 1));
        return animation;
    }

    private static void VerifyUndo(Action change, Action changed, Action restored)
    {
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        try
        {
            change();
            Undo.FlushUndoRecordObjects();
            changed();
            Undo.PerformUndo();
            restored();
            Undo.PerformRedo();
            changed();
        }
        finally
        {
            Undo.RevertAllDownToGroup(group);
            Undo.IncrementCurrentGroup();
        }
    }

    internal static string CaptureAuthoredSnapshot()
    {
        var paths = Directory.GetFiles("Assets/04_Timelines/Player", "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets/07_Datas/Player", "*", SearchOption.AllDirectories))
            .Concat(new[] { "Assets/03_Animations/Player/Controllers/PlayerAnimatorController.controller",
                "Assets/08_Prefabs/Combat/Player.prefab", "Assets/01_Scenes/CombatPrototypeScene.unity", "Assets/01_Scenes/DemoScene 1.unity" })
            .Where(File.Exists).OrderBy(path => path, StringComparer.Ordinal);
        var snapshot = new StringBuilder();
        using var hash = SHA256.Create();
        foreach (string path in paths)
            snapshot.Append(path).Append(':').Append(Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)))).Append('\n');
        return snapshot.ToString();
    }

    private static void DestroyTimeline(TimelineAsset timeline)
    {
        foreach (var track in PlayerSkillStudio.AllTracks(timeline).Reverse().ToArray())
        {
            foreach (var clip in track.GetClips().ToArray())
                if (clip.asset != null) { Undo.ClearUndo(clip.asset); Object.DestroyImmediate(clip.asset); }
            foreach (var marker in track.GetMarkers().OfType<Object>().ToArray())
            { Undo.ClearUndo(marker); Object.DestroyImmediate(marker); }
            Undo.ClearUndo(track);
            Object.DestroyImmediate(track);
        }
        Undo.ClearUndo(timeline);
        Object.DestroyImmediate(timeline);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Evidence.Add("PASS: " + message);
    }

    private static void Near(double actual, double expected, string message)
        => Check(Math.Abs(actual - expected) < .00001d, message + $" ({actual:0.#####})");
}
