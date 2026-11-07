using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Checks preview isolation and real attack attachments without evaluating a gameplay director.</summary>
public static class PlayerAttackStudioPreviewVerification
{
    public static string LastReport { get; private set; }
    private static readonly List<string> Evidence = new();

    [MenuItem("Tools/Character/Player/Validate Attack Studios Preview")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Attack preview verification requires Edit Mode.");
        var player = Object.FindObjectsByType<PlayerCore>(FindObjectsInactive.Include)
            .FirstOrDefault(value => value.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(value));
        if (player == null) throw new InvalidOperationException("Open a scene containing the player first.");
        Evidence.Clear();
        string filesBefore = PlayerAttackStudioAuthoringVerification.CaptureAuthoredSnapshot();
        var transforms = player.GetComponentsInChildren<Transform>(true);
        var positions = transforms.Select(t => t.localPosition).ToArray();
        var rotations = transforms.Select(t => t.localRotation).ToArray();
        var scales = transforms.Select(t => t.localScale).ToArray();
        var active = transforms.Select(t => t.gameObject.activeSelf).ToArray();
        bool dirtyBefore = player.gameObject.scene.isDirty;
        var controllerBefore = player.Animator.runtimeAnimatorController;
        using var preview = new PlayerAttackStudioPreview();
        try
        {
            var actions = PlayerAttackStudioData.GetActions(player, false)
                .Concat(PlayerAttackStudioData.GetActions(player, true));
            foreach (var action in actions)
            {
                double directorTime = action.Director.time;
                var directorState = action.Director.state;
                preview.Build(player, action.Director, action.Clip, action.Speed);
                Check(preview.IsReady && preview.ModelAnimator != player.Animator, action.Id + " uses an isolated model");
                var root = preview.ModelAnimator.transform.root;
                Check(root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                      root.GetComponentsInChildren<Collider>(true).Length == 0 &&
                      root.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                      root.GetComponentsInChildren<AudioSource>(true).Length == 0 &&
                      root.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true).Length == 0,
                    action.Id + " preview has no gameplay scripts, physics, audio or live directors");
                Check(!preview.ModelAnimator.fireEvents && preview.ModelAnimator.runtimeAnimatorController == null,
                    action.Id + " never sends animation events or executes the FSM controller");
                Check(root.GetComponentsInChildren<Transform>(true).All(t => (t.gameObject.hideFlags & HideFlags.DontSave) != 0),
                    action.Id + " entire preview hierarchy is excluded from scene saves");
                var hits = PlayerAttackStudioData.GetSignals(action).Where(row => row.IsHit).ToArray();
                foreach (var hit in hits)
                {
                    preview.Sample(hit.Marker.time);
                    Transform mapped = preview.ResolveTransform(hit.Field.Hitbox.transform);
                    Check(mapped != null && mapped != hit.Field.Hitbox.transform && mapped.IsChildOf(root),
                        action.Id + " hit " + hit.Marker.time.ToString("F3") + " resolves against sampled preview attachment");
                    Check(Vector3.Distance(mapped.localPosition, hit.Field.Hitbox.transform.localPosition) < .0001f,
                        action.Id + " preserves authored collider attachment offsets");
                }
                var effects = PlayerAttackStudioData.EnumerateTracks(action.Timeline)
                    .Where(t => !t.mutedInHierarchy).SelectMany(t => t.GetClips())
                    .Where(c => c.asset is ControlPlayableAsset control &&
                        (control.prefabGameObject != null || control.sourceGameObject.Resolve(action.Director) != null)).ToArray();
                Check(preview.EffectCount == effects.Length, action.Id + " resolves all active ControlTrack sources");
                foreach (double time in new[] { 0d, .35d, action.Duration + .1d })
                {
                    preview.Sample(time);
                    int expected = effects.Count(effect => time >= effect.start && time < effect.end);
                    Check(preview.VisibleEffectCount == expected, action.Id + " effect visibility follows Timeline at " + time.ToString("F3"));
                }
                preview.Sample(.4);
                Vector3 normalPosition = preview.CurrentPosition;
                Transform hand = preview.ModelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 normalHand = hand != null ? root.InverseTransformPoint(hand.position) : Vector3.zero;
                preview.Build(player, action.Director, action.Clip, action.Speed * 2);
                preview.Sample(.2);
                Transform fastHand = preview.ModelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                Check(Vector3.Distance(normalPosition, preview.CurrentPosition) < .001f &&
                      (fastHand == null || Vector3.Distance(normalHand, preview.ModelAnimator.transform.root.InverseTransformPoint(fastHand.position)) < .001f),
                    action.Id + " doubled motion speed reaches the same pose and root position in half the Timeline time");
                preview.Sample(.05); preview.Sample(.3); Vector3 firstScrub = preview.CurrentPosition;
                preview.Sample(.01); preview.Sample(.3);
                Check(Vector3.Distance(firstScrub, preview.CurrentPosition) < .0001f, action.Id + " backwards scrubbing is deterministic");
                Check(action.Director.time == directorTime && action.Director.state == directorState,
                    action.Id + " leaves the gameplay director stopped at its original time");
                GameObject cloneRoot = preview.ModelAnimator.transform.root.gameObject;
                preview.Dispose();
                Check(!preview.IsReady && cloneRoot == null, action.Id + " releases its model and effects on close");
            }
            Check(player.Animator.runtimeAnimatorController == controllerBefore, "Original Animator controller stays connected");
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i].localPosition != positions[i] || transforms[i].localRotation != rotations[i] ||
                    transforms[i].localScale != scales[i] || transforms[i].gameObject.activeSelf != active[i])
                    throw new InvalidOperationException("Preview altered authored transform: " + transforms[i].name);
            Check(true, "Every authored player transform and active state remains unchanged");
            Check(player.gameObject.scene.isDirty == dirtyBefore, "Preview does not dirty the gameplay scene");
            Check(filesBefore == PlayerAttackStudioAuthoringVerification.CaptureAuthoredSnapshot(),
                "Preview leaves all authored combat assets unchanged");
            LastReport = $"PASS: Attack Studios preview ({Evidence.Count} checks).";
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/PlayerAttackStudioPreviewVerification.result", LastReport + "\n" + string.Join("\n", Evidence));
            Debug.Log(LastReport);
        }
        catch (Exception exception)
        {
            LastReport = "FAIL: Attack Studios preview: " + exception;
            File.WriteAllText("Temp/PlayerAttackStudioPreviewVerification.result", LastReport + "\n" + string.Join("\n", Evidence));
            throw;
        }
    }

    private static void Check(bool condition, string evidence)
    {
        if (!condition) throw new InvalidOperationException(evidence);
        Evidence.Add(evidence);
    }
}
