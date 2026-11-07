using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Exercises authoring operations on temporary Timelines; never writes gameplay assets.</summary>
public static class PlayerSkillStudioAuthoringVerification
{
    private const string ReportPath = "Temp/PlayerSkillStudioAuthoringVerification.result";
    private const string TimelinePath = "Assets/04_Timelines/Player/SkillTimeline.playable";
    private static readonly List<string> Evidence = new();
    public static string LastReport { get; private set; }

    [MenuItem("Tools/Character/Player/Validate E Skill Studio Authoring")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("E authoring verification runs in Edit Mode.");
        Evidence.Clear();
        Directory.CreateDirectory("Temp");
        byte[] authoredBefore = File.ReadAllBytes(TimelinePath);
        try
        {
            ValidateStableIdentity();
            ValidateLegacyCompatibility();
            ValidateUpgrade();
            ValidateSourceTime();
            ValidatePreviewSelection();
            Check(authoredBefore.SequenceEqual(File.ReadAllBytes(TimelinePath)),
                "Authoring verification leaves the gameplay Timeline file unchanged");
            LastReport = $"PASS: E Skill Studio authoring ({Evidence.Count} checks).";
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            Debug.Log(LastReport);
        }
        catch (Exception exception)
        {
            LastReport = "FAIL: E Skill Studio authoring: " + exception;
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            throw;
        }
    }

    private static void ValidateStableIdentity()
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        try
        {
            var track = timeline.CreateTrack<PlayerSkillHitTrack>();
            var late = track.CreateMarker<PlayerSkillHitMarker>(1.2d);
            var early = track.CreateMarker<PlayerSkillHitMarker>(.2d);
            var equalTime = track.CreateMarker<PlayerSkillHitMarker>(.2d);
            var explicitHit = track.CreateMarker<PlayerSkillHitMarker>(.7d);
            explicitHit.ExplicitDamageFieldNumber = 9;
            var nativeOrder = track.GetMarkers().OfType<PlayerSkillHitMarker>().ToArray();
            var fields = nativeOrder.Select(hit => hit.DamageFieldNumber).ToArray();
            var times = nativeOrder.Select(hit => hit.time).ToArray();
            Check(fields.SequenceEqual(new[] { 4, 1, 2, 9 }),
                "Legacy field identity uses chronological time and original order for equal-time hits");
            Check(PlayerSkillTimelineUtility.OrderedHits(track).SequenceEqual(new[] { early, equalTime, explicitHit, late }),
                "Unmigrated rows begin in chronological order with stable equal-time ordering");

            VerifyUndo(() => Check(PlayerSkillTimelineUtility.StabilizeHitFields(track) == 4,
                    "Migration stabilizes every unmigrated row"),
                () =>
                {
                    Check(nativeOrder.Select(hit => hit.ExplicitDamageFieldNumber).SequenceEqual(fields),
                        "Migration captures effective IDs before changing any marker");
                    Check(PlayerSkillTimelineUtility.OrderedHits(track).Select(hit => hit.DisplayOrder).SequenceEqual(new[] { 0, 1, 2, 3 }),
                        "Migration initializes contiguous independent row order");
                },
                () => Check(late.ExplicitDamageFieldNumber == 0 && early.ExplicitDamageFieldNumber == 0 &&
                    equalTime.ExplicitDamageFieldNumber == 0 && explicitHit.ExplicitDamageFieldNumber == 9 &&
                    nativeOrder.All(hit => hit.DisplayOrder == -1), "One Undo restores pre-migration identity and row metadata"));

            PlayerSkillTimelineUtility.StabilizeHitFields(track);
            Check(PlayerSkillTimelineUtility.StabilizeHitFields(track) == 0, "Repeated migration is a no-op");
            VerifyUndo(() => Check(PlayerSkillTimelineUtility.MoveHit(late, -1), "A hit row can move before its neighbor"),
                () =>
                {
                    Check(PlayerSkillTimelineUtility.OrderedHits(track).SequenceEqual(new[] { early, equalTime, late, explicitHit }),
                        "Row move changes display order only");
                    Check(track.GetMarkers().OfType<PlayerSkillHitMarker>().SequenceEqual(nativeOrder),
                        "Row move preserves Timeline notification enumeration including same-time events");
                    Check(nativeOrder.Select(hit => hit.time).SequenceEqual(times) &&
                        nativeOrder.Select(hit => hit.DamageFieldNumber).SequenceEqual(fields),
                        "Row move preserves attack timestamps and bound damage fields");
                },
                () => Check(PlayerSkillTimelineUtility.OrderedHits(track).SequenceEqual(new[] { early, equalTime, explicitHit, late }),
                    "One Undo restores the original hit row order"));

            VerifyUndo(() => Check(PlayerSkillTimelineUtility.SetMarkerTime(late, .05d), "Hit marker can cross every earlier event"),
                () =>
                {
                    Near(late.time, .05d, "Time edit reaches the requested timestamp");
                    Check(nativeOrder.Select(hit => hit.DamageFieldNumber).SequenceEqual(fields),
                        "Crossing earlier markers cannot silently reassign any damage field");
                    Check(PlayerSkillTimelineUtility.OrderedHits(track).SequenceEqual(new[] { early, equalTime, explicitHit, late }),
                        "Moving time leaves display order unchanged");
                },
                () => Near(late.time, 1.2d, "One Undo restores a marker's previous time"));

            Check(!PlayerSkillTimelineUtility.MoveHit(early, -1) && !PlayerSkillTimelineUtility.MoveHit(late, 1),
                "Boundary row moves are no-ops");
            Check(!PlayerSkillTimelineUtility.SetMarkerTime(late, double.NaN) &&
                !PlayerSkillTimelineUtility.SetMarkerTime(late, double.PositiveInfinity),
                "Nonfinite marker times are rejected");
            Near(late.time, 1.2d, "Rejected marker edits preserve the timestamp");
            PlayerSkillTimelineUtility.SetMarkerTime(late, -3d);
            Near(late.time, 0d, "Negative marker time clamps to the Timeline start");
            Check(PlayerSkillTimelineUtility.NextUnusedField(track) == 3,
                "New hits choose an unoccupied damage field instead of changing existing identities");
        }
        finally { DestroyTimeline(timeline); }
    }

    private static void ValidateLegacyCompatibility()
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        try
        {
            var track = timeline.CreateTrack<PlayerSkillHitTrack>();
            var hits = Enumerable.Range(0, 14).Select(index => track.CreateMarker<PlayerSkillHitMarker>(index * .1d)).ToArray();
            Check(hits.All(hit => hit.Enabled), "Existing and newly created hit markers are enabled by default");
            hits[0].Enabled = false;
            Check(hits[1].DamageFieldNumber == 2, "Disabled legacy rows still occupy their original automatic field rank");
            Check(hits[12].DamageFieldNumber == 12 && hits[13].DamageFieldNumber == 12,
                "Legacy overflow markers still resolve to field 12");
            PlayerSkillTimelineUtility.StabilizeHitFields(track);
            Check(!hits[0].Enabled && hits[1].ExplicitDamageFieldNumber == 2,
                "Stabilization preserves enabled state and neighboring field identity");
            Check(PlayerSkillTimelineUtility.NextUnusedField(track) == 12,
                "A full set of fields retains the compatible field-12 fallback");
            var serialized = new SerializedObject(hits[0]);
            Check(serialized.FindProperty("_enabled") != null && !serialized.FindProperty("_enabled").boolValue,
                "Enabled state is serialized on the Timeline marker");
            Check(hits.All(hit => hit.flags == NotificationFlags.TriggerOnce),
                "Markers remain once-per-playback and do not opt into edit-mode notification delivery");
        }
        finally { DestroyTimeline(timeline); }
    }

    private static void ValidateUpgrade()
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var signal = ScriptableObject.CreateInstance<SignalAsset>();
        try
        {
            var group = timeline.CreateTrack<GroupTrack>(null, "Nested E presentation");
            var hits = timeline.CreateTrack<PlayerSkillHitTrack>(group, "Nested hits");
            var a = hits.CreateMarker<PlayerSkillHitMarker>(.8d);
            var b = hits.CreateMarker<PlayerSkillHitMarker>(.4d);
            var effects = timeline.CreateTrack<ControlTrack>();
            var effect = effects.CreateClip<ControlPlayableAsset>();
            effect.start = .3d;
            effect.duration = 1.7d;
            var signals = timeline.CreateTrack<SignalTrack>();
            var cue = signals.CreateMarker<SignalEmitter>(.35d);
            cue.asset = signal;
            var beforeDuration = timeline.duration;
            Check(PlayerSkillStudioSetup.UpgradeTimeline(timeline) == 2,
                "Upgrade discovers hit tracks nested inside Timeline groups");
            Check(a.ExplicitDamageFieldNumber == 2 && b.ExplicitDamageFieldNumber == 1,
                "Upgrade preserves nested markers' effective field mapping");
            Near(a.time, .8d, "Upgrade preserves the later authored hit time");
            Near(b.time, .4d, "Upgrade preserves the earlier authored hit time");
            Near(effect.start, .3d, "Upgrade leaves effect start times intact");
            Near(effect.duration, 1.7d, "Upgrade leaves effect durations intact");
            Near(cue.time, .35d, "Upgrade leaves signal timestamps intact");
            Check(cue.asset == signal, "Upgrade preserves the signal asset reference");
            Near(timeline.duration, beforeDuration, "Upgrade preserves the existing sequence duration");
            Check(PlayerSkillStudioSetup.UpgradeTimeline(timeline) == 0, "Upgrading the same Timeline twice makes no further edits");
        }
        finally { DestroyTimeline(timeline); Object.DestroyImmediate(signal); }
    }

    private static void ValidateSourceTime()
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var animation = new AnimationClip();
        try
        {
            // Timeline deliberately disables ClipIn for empty animations. Use a real
            // four-second curve so the fixture exercises the same capabilities as authored clips.
            AnimationUtility.SetEditorCurve(animation,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, 4f, 1f));
            var track = timeline.CreateTrack<AnimationTrack>();
            var clip = track.CreateClip<AnimationPlayableAsset>();
            ((AnimationPlayableAsset)clip.asset).clip = animation;
            clip.start = 2d;
            clip.duration = 1.5d;
            clip.clipIn = .4d;
            clip.timeScale = 2d;
            Near(PlayerSkillTimelineUtility.TimelineToSourceTime(clip, 2d), .4d, "Clip entry maps to its trimmed source start");
            Near(PlayerSkillTimelineUtility.TimelineToSourceTime(clip, 2.5d), 1.4d, "Source inspection accounts for clip speed");
            Near(PlayerSkillTimelineUtility.SourceToTimelineTime(clip, 1.4d), 2.5d, "Source-time scrubbing returns the same Timeline pose");
            Near(PlayerSkillTimelineUtility.TimelineToSourceTime(clip, -5d), .4d, "Before-clip source inspection clamps to the trim start");
            Near(PlayerSkillTimelineUtility.TimelineToSourceTime(clip, 99d), 3.4d, "After-clip source inspection clamps to the trimmed end");
            Near(PlayerSkillTimelineUtility.SourceToTimelineTime(clip, 0d), 2d, "Source seek before trim cannot enter the previous clip");
            Near(PlayerSkillTimelineUtility.SourceToTimelineTime(clip, 99d), 3.5d, "Source seek after trim cannot enter a later clip");
            clip.timeScale = .5d;
            Near(PlayerSkillTimelineUtility.SourceToTimelineTime(clip, .65d), 2.5d, "Slow clip source seeking applies the inverse speed");
        }
        finally { DestroyTimeline(timeline); Object.DestroyImmediate(animation); }
    }

    private static void ValidatePreviewSelection()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        PlayerSkillStudio studio = null;
        try
        {
            timeline.editorSettings.frameRate = 60;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 4d;
            var animationTrack = timeline.CreateTrack<AnimationTrack>();
            var clip = animationTrack.CreateClip<AnimationPlayableAsset>();
            clip.start = 2d;
            clip.duration = 1.5d;
            var group = timeline.CreateTrack<GroupTrack>();
            var hits = timeline.CreateTrack<PlayerSkillHitTrack>(group, "Hit visibility");
            var hit = hits.CreateMarker<PlayerSkillHitMarker>(.5d);
            var host = new GameObject("E Studio preview selection verification");
            SceneManager.MoveGameObjectToScene(host, scene);
            var director = host.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = timeline;
            studio = ScriptableObject.CreateInstance<PlayerSkillStudio>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object value) => typeof(PlayerSkillStudio).GetField(name, flags).SetValue(studio, value);
            double Value(string name) => (double)typeof(PlayerSkillStudio).GetProperty(name, flags).GetValue(studio);
            bool Visible() => (bool)typeof(PlayerSkillStudio).GetMethod("IsHitActive", flags).Invoke(studio, new object[] { hit });
            Set("_director", director);
            Set("_animationClip", clip);
            Set("_focusSelectedClip", true);
            Near(Value("PreviewStart"), 2d, "Selected-clip preview begins at its actual Timeline start");
            Near(Value("PreviewEnd"), 3.5d, "Selected-clip preview ends at its actual Timeline end");
            Set("_focusSelectedClip", false);
            Near(Value("PreviewStart"), 0d, "Whole-sequence preview begins at zero");
            Near(Value("PreviewEnd"), timeline.duration, "Whole-sequence preview covers the full authored duration");
            Set("_time", .5d);
            Check(Visible(), "Hitbox visibility is active at the selected marker frame");
            Set("_time", .5d + 1d / 60d);
            Check(!Visible(), "Hitbox visibility is hidden one frame after the marker");
            Set("_time", .5d - 1d / 60d);
            Check(!Visible(), "Hitbox visibility is hidden one frame before the marker");
            Set("_time", .5d);
            hit.Enabled = false;
            Check(!Visible(), "Disabled hit rows cannot display editable hitboxes");
            hit.Enabled = true;
            hits.muted = true;
            Check(!Visible(), "Muted hit tracks cannot display editable hitboxes");
            hits.muted = false;
            group.muted = true;
            Check(!Visible(), "Muted parent groups suppress descendant hitbox editing");
        }
        finally
        {
            if (studio != null) Object.DestroyImmediate(studio);
            EditorSceneManager.ClosePreviewScene(scene);
            DestroyTimeline(timeline);
        }
    }

    /// <summary>Called by the existing Play Mode verifier; all attack data and actors are temporary.</summary>
    public static string RunEnabledHitRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Runtime hit verification requires Play Mode.");
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var root = new GameObject("E Studio enabled-hit verification");
        PlayableDirector director = null;
        try
        {
            root.SetActive(false);
            root.transform.position = new Vector3(12000, 12000, 12000);
            root.AddComponent<PlayerSkillHitTestActor>();
            var container = root.AddComponent<PlayerAttackInstanceContainer>();
            director = root.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            var receiver = root.AddComponent<PlayerSkillAttackReceiver>();
            var hitboxObject = new GameObject("Query-only hitbox");
            hitboxObject.transform.SetParent(root.transform, false);
            var hitbox = hitboxObject.AddComponent<BoxCollider>();
            hitbox.size = Vector3.one * 4;
            hitbox.enabled = false;
            var settings = new SerializedObject(receiver);
            settings.FindProperty("_attackContainer").objectReferenceValue = container;
            var fields = settings.FindProperty("_damageFields");
            fields.arraySize = PlayerSkillAttackReceiver.DamageFieldCount;
            var field = fields.GetArrayElementAtIndex(0);
            field.FindPropertyRelative("_hitbox").objectReferenceValue = hitbox;
            field.FindPropertyRelative("_damage").floatValue = 7;
            field.FindPropertyRelative("_hitStopFrame").intValue = 0;
            field.FindPropertyRelative("_targetLayers").intValue = 1 << 6;
            field.FindPropertyRelative("_triggerInteraction").intValue = (int)QueryTriggerInteraction.Collide;
            settings.ApplyModifiedPropertiesWithoutUndo();

            var victimObject = new GameObject("Victim");
            victimObject.transform.SetParent(root.transform, false);
            victimObject.layer = 6;
            victimObject.AddComponent<BoxCollider>().isTrigger = true;
            victimObject.AddComponent<SphereCollider>().isTrigger = true;
            var victim = victimObject.AddComponent<PlayerSkillHitTestActor>();
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2;
            var track = timeline.CreateTrack<PlayerSkillHitTrack>();
            var disabled = track.CreateMarker<PlayerSkillHitMarker>(.5d);
            disabled.ExplicitDamageFieldNumber = 1;
            disabled.Enabled = false;
            var enabled = track.CreateMarker<PlayerSkillHitMarker>(1d);
            enabled.ExplicitDamageFieldNumber = 1;
            director.playableAsset = timeline;
            director.SetGenericBinding(track, receiver);
            root.SetActive(true);
            Physics.SyncTransforms();
            director.Play();
            Check(director.state == PlayState.Playing && director.playableGraph.IsValid(),
                "Runtime enabled-hit check uses a playing, bound Timeline graph");
            var origin = director.playableGraph.GetRootPlayable(0);
            receiver.OnNotify(origin, disabled, null);
            Check(victim.Hits.Count == 0 && victim.DamageAttempts == 0,
                "Disabled marker does not query or damage an overlapping target");
            receiver.OnNotify(origin, enabled, null);
            Check(victim.Hits.Count == 1 && Math.Abs(victim.Health - 993f) < .001f,
                "Enabled marker uses the real damage receiver and deduplicates the target's two colliders");
            disabled.Enabled = true;
            receiver.OnNotify(origin, disabled, null);
            Check(victim.Hits.Count == 2 && Math.Abs(victim.Health - 986f) < .001f,
                "Re-enabling a marker restores its original damage-field behavior");
            director.Pause();
            receiver.OnNotify(origin, enabled, null);
            Check(victim.Hits.Count == 2, "Paused Timeline still suppresses damage notifications");
            return "PASS: disabled hit suppression, enabled damage, collider deduplication, re-enable, paused guard (temporary runtime actors).";
        }
        finally
        {
            if (director != null) director.Stop();
            Object.DestroyImmediate(root);
            DestroyTimeline(timeline);
        }
    }

    private static void VerifyUndo(Action change, Action changed, Action restored)
    {
        // Tests own only this newly created group; the user's existing undo history stays intact.
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

    private static void DestroyTimeline(TimelineAsset timeline)
    {
        if (timeline == null) return;
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
