using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises real attack discovery, reversible event editing, and preview isolation.</summary>
public static class EnemyAttackStudioVerification
{
    [MenuItem("Tools/Character/Enemy/Validate Attack Studio")]
    private static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("적 공격 에디터 검증은 Edit Mode에서 실행하세요.");

        int checks = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition) throw new InvalidOperationException("Enemy Attack Studio: " + message);
            checks++;
        };
        var enemy = UnityEngine.Object.FindObjectsByType<EnemyCore>(FindObjectsInactive.Include)
            .Where(e => e.gameObject.scene.IsValid() && (e.hideFlags & HideFlags.HideAndDontSave) == 0)
            .OrderBy(e => e.name).FirstOrDefault();
        check(enemy != null, "A real scene enemy is available");
        var attacks = EnemyAttackStudioData.FindAttacks(enemy);
        var first = attacks.FirstOrDefault(a => a.AttackID == EnemyAttackID.CloseAttack1Hit);
        var second = attacks.FirstOrDefault(a => a.AttackID == EnemyAttackID.CloseAttack2Hit);
        check(first != null && second != null, "Both runtime combo attacks are discovered");
        var firstClips = EnemyAttackStudioData.FindClips(enemy, first);
        var secondClips = EnemyAttackStudioData.FindClips(enemy, second);
        check(firstClips.Length > 0 && secondClips.Length > 0, "Both attacks resolve to actual Animator event clips");
        var source = firstClips[0];
        check(EnemyAttackStudioData.GetHitTime(source, first) >= 0, "The first hit event exists");
        check(EnemyAttackStudioData.GetHitTime(secondClips[0], second) >= 0, "The second hit event exists");
        check(EnemyAttackStudioData.GetHitTime(source, second) == -1, "An unrelated attack is not matched by name alone");

        var sourceEvents = AnimationUtility.GetAnimationEvents(source);
        var originalTransforms = enemy.GetComponentsInChildren<Transform>(true);
        var originalPose = originalTransforms.Select(t => new PoseSnapshot(t)).ToArray();
        bool originalSceneDirty = enemy.gameObject.scene.isDirty;
        string originalAttackJson = EditorJsonUtility.ToJson(first);
        int previewRootsBefore = PreviewRootCount();
        string folder = "Assets/__EnemyAttackStudioVerification_" + Guid.NewGuid().ToString("N");
        AnimationClip editable = null;
        EnemyAttackSO warpData = null;
        EnemyAttackStudioPreview preview = null;
        try
        {
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            editable = UnityEngine.Object.Instantiate(source);
            editable.name = "Temporary enemy event verification";
            AssetDatabase.CreateAsset(editable, folder + "/EventTest.anim");
            AnimationUtility.SetAnimationEvents(editable, new[]
            {
                new AnimationEvent { time = .1f, functionName = "__UnrelatedEarly", stringParameter = "preserve early", floatParameter = 1.25f, intParameter = 9, messageOptions = SendMessageOptions.DontRequireReceiver },
                new AnimationEvent { time = .2f, functionName = "OnAttack", objectReferenceParameter = first, stringParameter = "preserve hit payload", floatParameter = 7.5f, intParameter = 21, messageOptions = SendMessageOptions.DontRequireReceiver },
                new AnimationEvent { time = .65f, functionName = "__SameTimeA", stringParameter = "A", intParameter = 41 },
                new AnimationEvent { time = .65f, functionName = "__SameTimeB", stringParameter = "B", intParameter = 42 }
            });
            AssetDatabase.SaveAssetIfDirty(editable);
            var before = AnimationUtility.GetAnimationEvents(editable);
            check(EnemyAttackStudioData.CanEditEvents(editable), "A standalone .anim is editable");

            Undo.IncrementCurrentGroup();
            check(EnemyAttackStudioData.SetAttackEventTime(editable, first, .65f), "Hit timing can move across other events");
            Undo.FlushUndoRecordObjects();
            var after = AnimationUtility.GetAnimationEvents(editable);
            check(after.Length == before.Length, "Event editing preserves event count");
            check(after.All(e => SamePayload(e, before.Single(b => b.functionName == e.functionName))), "All event payloads survive a timing edit");
            check(after.Where(e => e.functionName != "OnAttack").All(e => Near(e.time, before.Single(b => b.functionName == e.functionName).time)), "Other event times are unchanged");
            check(after.Select(e => e.time).SequenceEqual(after.Select(e => e.time).OrderBy(t => t)), "Events remain sorted");
            check(after.Where(e => Near(e.time, .65f)).Select(e => e.functionName).SequenceEqual(new[] { "OnAttack", "__SameTimeA", "__SameTimeB" }), "Equal-time events preserve stable order");
            Undo.PerformUndo();
            check(Near(EnemyAttackStudioData.GetHitTime(editable, first), .2f), "Undo restores the previous hit time");
            Undo.PerformRedo();
            check(Near(EnemyAttackStudioData.GetHitTime(editable, first), .65f), "Redo restores the edited hit time");
            Undo.IncrementCurrentGroup();
            check(EnemyAttackStudioData.SetAttackEventTime(editable, first, -4) && Near(EnemyAttackStudioData.GetHitTime(editable, first), 0), "Negative timing clamps to zero");
            check(EnemyAttackStudioData.SetAttackEventTime(editable, first, editable.length + 100) && Near(EnemyAttackStudioData.GetHitTime(editable, first), editable.length), "Late timing clamps to clip duration");
            check(!EnemyAttackStudioData.SetAttackEventTime(editable, first, float.NaN), "NaN timing is rejected");
            check(!EnemyAttackStudioData.SetAttackEventTime(editable, second, .5f), "Missing attack events are never created implicitly");
            var imported = FindImportedClip();
            check(imported != null, "A project model animation is available for the read-only check");
            check(!EnemyAttackStudioData.CanEditEvents(imported), "Imported model animations cannot be edited as standalone clips");
            check(!EnemyAttackStudioData.SetAttackEventTime(imported, first, .5f), "Imported model event writes are rejected");

            preview = new EnemyAttackStudioPreview();
            preview.Build(enemy);
            check(preview.Ready && preview.ModelAnimator != null, "Isolated animated preview builds");
            var model = preview.ModelAnimator.gameObject;
            check(model != enemy.GetComponentInChildren<Animator>(true).gameObject, "Preview owns a copied visual model");
            check(model.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Gameplay scripts cannot run on the preview");
            check(model.GetComponentsInChildren<Collider>(true).Length == 0 && model.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Preview model contains no gameplay physics");
            check(model.GetComponentsInChildren<AudioSource>(true).Length == 0 && !preview.ModelAnimator.fireEvents, "Preview cannot emit animation events or audio");
            preview.SamplePose(source, 0, false);
            var bones = model.GetComponentsInChildren<Transform>(true);
            var atStart = bones.Select(t => t.localRotation).ToArray();
            preview.SamplePose(source, Mathf.Min(.45f, source.length), false);
            check(bones.Where((t, i) => Quaternion.Angle(t.localRotation, atStart[i]) > .1f).Any(), "Scrubbing changes the skeleton pose");
            check(Near(preview.SampledTime, Mathf.Min(.45f, source.length)), "Preview samples the requested source time");
            check(preview.RootPosition == Vector3.zero, "In-place mode keeps the preview root fixed");
            preview.SamplePose(source, Mathf.Min(.45f, source.length), true);
            check(IsFinite(preview.RootPosition), "Optional authored root movement yields finite positions");

            warpData = UnityEngine.Object.Instantiate(first);
            warpData.AttackWarpDistance = 5;
            warpData.HitboxInfo = new[] { new EnemyAttackHitboxInfo { Offset = new Vector3(0, 1, 1), Size = Vector3.one } };
            preview.SampleAttackPose(source, 0, warpData, 20, EnemyAttackPreviewMotion.AttackWarp);
            check(Near(preview.PlannedWarpDistance, 5) && preview.RootPosition == Vector3.zero, "Far targets show the configured maximum but start at the origin");
            preview.SampleAttackPose(source, source.length, warpData, 3, EnemyAttackPreviewMotion.AttackWarp);
            check(Near(preview.PlannedWarpDistance, 2), "Close targets shorten the lunge using the primary hitbox offset");
            check(Near(preview.RootPosition.z, 2 * Mathf.Clamp01(preview.AuthoredPlanarTravel)), "Warp sampling preserves authored acceleration and clamps at the destination");
            check(preview.RootPosition.z > .01f && preview.RootPosition.z <= 2, "The actual attack clip visibly lunges without passing its destination");
            Vector3 endPosition = preview.RootPosition;
            preview.SampleAttackPose(source, source.length * .5f, warpData, 3, EnemyAttackPreviewMotion.AttackWarp);
            Vector3 middlePosition = preview.RootPosition;
            preview.SampleAttackPose(source, 0, warpData, 3, EnemyAttackPreviewMotion.AttackWarp);
            check(preview.RootPosition == Vector3.zero, "Backward scrubbing resets lunge movement without accumulating drift");
            preview.SampleAttackPose(source, source.length * .5f, warpData, 3, EnemyAttackPreviewMotion.AttackWarp);
            check((preview.RootPosition - middlePosition).sqrMagnitude < .000001f && preview.RootPosition.z <= endPosition.z, "Arbitrary scrubbing yields deterministic forward progress");
            preview.SampleAttackPose(source, source.length, warpData, .5f, EnemyAttackPreviewMotion.AttackWarp);
            check(preview.PlannedWarpDistance == 0 && preview.RootPosition == Vector3.zero, "Targets already behind the primary hitbox never cause backward travel");
            warpData.AttackWarpDistance = 0;
            preview.SampleAttackPose(source, source.length, warpData, 20, EnemyAttackPreviewMotion.AttackWarp);
            check(preview.PlannedWarpDistance == 0 && preview.RootPosition == Vector3.zero, "Setting maximum lunge to zero immediately removes movement");
            warpData.AttackWarpDistance = 5;
            preview.SampleAttackPose(source, source.length, warpData, 20, EnemyAttackPreviewMotion.InPlace);
            check(preview.RootPosition == Vector3.zero, "In-place comparison mode remains stationary");
            var syntheticRoot = new UltimateSequenceClip { rootSamples = new List<UltimateRootSample>
            {
                new UltimateRootSample { time = 0, position = Vector3.zero },
                new UltimateRootSample { time = 1, position = new Vector3(0, 4, .25f) },
                new UltimateRootSample { time = 2, position = new Vector3(0, 8, 0) }
            } };
            check(Near(EnemyAttackStudioPreview.PlanarTravel(syntheticRoot, 2), .5f), "Planar deltas accumulate even when authored root motion reverses direction; height is excluded");
            check(Near(EnemyAttackStudioPreview.PlanarTravel(syntheticRoot, .5f), .125f), "Subframe sampling interpolates partial root movement");
            check(EnemyAttackStudioPreview.PlanarTravel(new UltimateSequenceClip(), 5) == 0, "Clips without root motion cannot invent a lunge");
            preview.pitch = 30;
            preview.Orbit(new Vector2(0, -10));
            check(preview.pitch < 30, "Dragging upward rotates upward with the corrected vertical input");
            preview.Orbit(new Vector2(0, 20));
            check(preview.pitch > 30, "Dragging downward rotates downward");
            preview.Orbit(new Vector2(0, 10000));
            check(Near(preview.pitch, 85), "Orbit retains the upper pitch limit");
            preview.Orbit(new Vector2(0, -10000));
            check(Near(preview.pitch, -10), "Orbit retains the lower pitch limit");
            var previewAnimator = preview.ModelAnimator;
            preview.Dispose();
            check(!preview.Ready && preview.ModelAnimator == null && previewAnimator == null, "Disposal destroys the cloned Animator");
            check(PreviewRootCount() == previewRootsBefore, "Disposal removes all preview roots created by the test");
            check(originalPose.All(p => p.Unchanged()), "Source enemy transforms and skeleton pose are untouched");
            check(enemy.gameObject.scene.isDirty == originalSceneDirty, "Preview does not change the scene dirty state");
            check(EditorJsonUtility.ToJson(first) == originalAttackJson, "Source attack data is unchanged");
            var actualSourceEvents = AnimationUtility.GetAnimationEvents(source);
            check(actualSourceEvents.Length == sourceEvents.Length && actualSourceEvents.Where((e, i) => !SamePayload(e, sourceEvents[i]) || !Near(e.time, sourceEvents[i].time)).Count() == 0, "Source animation events are unchanged");
            return $"Enemy Attack Studio: {checks} checks passed. Discovery, event editing/Undo, preview isolation, lunge distance/scrubbing and camera orbit verified.";
        }
        finally
        {
            preview?.Dispose();
            if (warpData != null) UnityEngine.Object.DestroyImmediate(warpData);
            if (editable != null) Undo.ClearUndo(editable);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .0001f;
    private static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    private static bool SamePayload(AnimationEvent a, AnimationEvent b) => a.functionName == b.functionName && a.stringParameter == b.stringParameter && Near(a.floatParameter, b.floatParameter) && a.intParameter == b.intParameter && a.objectReferenceParameter == b.objectReferenceParameter && a.messageOptions == b.messageOptions;
    private static int PreviewRootCount() => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name.StartsWith("Enemy Attack Studio - disposable", StringComparison.Ordinal));

    private static AnimationClip FindImportedClip()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Download Assets" }).Take(100))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip != null) return clip;
        }
        return null;
    }

    private readonly struct PoseSnapshot
    {
        private readonly Transform _transform;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        public PoseSnapshot(Transform transform) { _transform = transform; _position = transform.localPosition; _rotation = transform.localRotation; _scale = transform.localScale; }
        public bool Unchanged() => _transform != null && (_transform.localPosition - _position).sqrMagnitude < .00000001f && SameRotation(_transform.localRotation, _rotation) && (_transform.localScale - _scale).sqrMagnitude < .00000001f;
        private static bool SameRotation(Quaternion a, Quaternion b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z) + Mathf.Abs(a.w - b.w) < .00001f;
    }
}
