using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Carries an outgoing Q/E pose into the next animation without delaying input
/// or adding root motion. Capture before releasing the current pose owner.
/// </summary>
[DefaultExecutionOrder(10000), DisallowMultipleComponent]
public sealed class PlayerUltimateExitBlend : MonoBehaviour
{
    private struct BonePose
    {
        public Transform bone;
        public Vector3 sourcePosition, targetPosition, displayedPosition;
        public Quaternion sourceRotation, targetRotation, displayedRotation;
    }

    private readonly List<BonePose> _bones = new();
    private Animator _animator;
    private PlayerCore _owner;
    private float _duration, _elapsed;
    private int _startFrame;
    private bool _hasTarget;

    public bool IsBlending { get; private set; }
    public float Progress => _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

    public static void Begin(Animator animator, float duration, PlayerCore owner)
    {
        if (animator == null) return;
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f ||
            !animator.isActiveAndEnabled || owner == null || !owner.isActiveAndEnabled)
        {
            Cancel(animator);
            return;
        }

        var blend = animator.GetComponent<PlayerUltimateExitBlend>();
        if (blend == null) blend = animator.gameObject.AddComponent<PlayerUltimateExitBlend>();
        blend.Capture(animator, duration, owner);
    }

    public static void Cancel(Animator animator)
    {
        if (animator != null && animator.TryGetComponent<PlayerUltimateExitBlend>(out var blend))
            blend.Clear(true);
    }

    private void Capture(Animator animator, float duration, PlayerCore owner)
    {
        // A repeated capture starts from the pose actually visible this frame,
        // including an earlier blend, rather than snapping to its destination.
        _bones.Clear();
        _animator = animator;
        _owner = owner;
        _duration = duration;
        _elapsed = 0f;
        _startFrame = Time.frameCount;
        _hasTarget = false;
        var skeleton = new HashSet<Transform>();
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var bone in renderer.bones) AddBoneAndParents(bone, animator.transform, skeleton);
        if (animator.isHuman)
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                AddBoneAndParents(animator.GetBoneTransform((HumanBodyBones)i), animator.transform, skeleton);

        foreach (Transform bone in skeleton)
            _bones.Add(new BonePose
            {
                bone = bone,
                sourcePosition = bone.localPosition,
                sourceRotation = bone.localRotation,
                displayedPosition = bone.localPosition,
                displayedRotation = bone.localRotation
            });
        IsBlending = _bones.Count > 0;
        enabled = IsBlending;
    }

    private static void AddBoneAndParents(Transform bone, Transform model, HashSet<Transform> skeleton)
    {
        if (bone == null || bone == model || !bone.IsChildOf(model)) return;
        while (bone != null && bone != model)
        {
            skeleton.Add(bone);
            bone = bone.parent;
        }
    }

    private void Update()
    {
        if (!IsBlending) return;
        if (!OwnerAvailable()) { Clear(true); return; }
        // Normal Animator evaluation follows Update. Restore its previous raw
        // output first so bones not keyed by the next clip do not blend repeatedly
        // from our own modified output and accumulate drift.
        // E's manual Timeline already supplied this frame's raw target in
        // PlayerCore.Update. Preserve that pose for the outgoing Q blend rather
        // than replacing it with the preceding frame's target before LateUpdate.
        if (_owner.StateMachine?.SkillState?.Runtime?.OwnsModelAnimation != true)
            RestoreTarget();
    }

    private void LateUpdate()
    {
        if (!IsBlending) return;
        if (!OwnerAvailable()) { Clear(true); return; }
        bool paused = _owner.IsHitStopped || VirtualCutscenePlayback.IsPlaying ||
            ProjectR.UI.PauseSettingsController.InputBlocked || Time.timeScale <= 0f;
        if (paused)
        {
            // Animator may still evaluate at speed zero. Keep the last displayed
            // pose rather than revealing its unblended destination during a hold.
            foreach (BonePose pose in _bones)
                if (pose.bone != null)
                    pose.bone.SetLocalPositionAndRotation(pose.displayedPosition, pose.displayedRotation);
            return;
        }

        if (Time.frameCount != _startFrame)
            _elapsed = Mathf.Min(_duration, _elapsed + Mathf.Max(0f, CombatTimeController.DeltaTime));
        float weight = Mathf.SmoothStep(0f, 1f, Progress);
        for (int i = 0; i < _bones.Count; i++)
        {
            BonePose pose = _bones[i];
            if (pose.bone == null) continue;
            pose.targetPosition = pose.bone.localPosition;
            pose.targetRotation = pose.bone.localRotation;
            pose.displayedPosition = Vector3.Lerp(pose.sourcePosition, pose.targetPosition, weight);
            pose.displayedRotation = Quaternion.Slerp(pose.sourceRotation, pose.targetRotation, weight);
            pose.bone.SetLocalPositionAndRotation(pose.displayedPosition, pose.displayedRotation);
            _bones[i] = pose;
        }
        _hasTarget = true;
        if (_elapsed >= _duration) Clear(false);
    }

    private bool OwnerAvailable() => _owner != null && _owner.isActiveAndEnabled &&
        _animator != null && _animator.isActiveAndEnabled;

    private void RestoreTarget()
    {
        if (!_hasTarget) return;
        foreach (BonePose pose in _bones)
            if (pose.bone != null)
                pose.bone.SetLocalPositionAndRotation(pose.targetPosition, pose.targetRotation);
    }

    private void Clear(bool restoreTarget)
    {
        if (restoreTarget) RestoreTarget();
        IsBlending = false;
        _hasTarget = false;
        _bones.Clear();
        _animator = null;
        _owner = null;
        enabled = false;
    }

    private void OnDisable() => Clear(true);
    private void OnDestroy() => Clear(true);
}
