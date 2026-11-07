using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Visual-only attack preview. Never evaluates the scene Animator or sends Timeline signals.</summary>
internal sealed class PlayerAttackStudioPreview : IDisposable
{
    private PlayerCore _player;
    private GameObject _root, _model;
    private Transform _playerProxy;
    private Animator _animator;
    private PlayableGraph _graph;
    private AnimationClipPlayable _playable;
    private AnimationClip _clip;
    private float _speed;
    private UltimateSequenceClip _rootMotion;
    private readonly Dictionary<Transform, Transform> _transforms = new();
    private readonly HashSet<Transform> _animated = new();
    private readonly HashSet<Transform> _external = new();
    private readonly List<Effect> _effects = new();
    private Transform _sourceAnimator;

    private sealed class Effect
    {
        public TimelineClip Clip;
        public TrackAsset Track;
        public ControlPlayableAsset Control;
        public GameObject Clone;
        public Transform Source;
        public bool IsPrefab;
        public ParticleSystem[] Roots;
    }

    public bool IsReady => _root != null && _graph.IsValid();
    public Vector3 Origin { get; private set; }
    public Quaternion Facing { get; private set; }
    public Vector3 CurrentPosition => _root != null ? _root.transform.position : Origin;
    public Animator ModelAnimator => _animator;
    public double SampledTime { get; private set; }
    public int EffectCount => _effects.Count;
    public int VisibleEffectCount => _effects.Count(effect => effect.Clone != null && effect.Clone.activeInHierarchy);

    public void Build(PlayerCore player, PlayableDirector director, AnimationClip clip, float speed)
    {
        Dispose();
        if (player == null || player.Animator == null || clip == null) return;
        if (player.Animator.GetComponentsInChildren<PlayableDirector>(true).Length != 0)
            throw new InvalidOperationException("모델 하위의 PlayableDirector를 분리한 뒤 미리보기를 실행하세요.");
        try
        {
            _player = player;
            _clip = clip;
            _speed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : Mathf.Max(.001f, speed);
            _sourceAnimator = player.Animator.transform;
            Vector3 forward = Vector3.ProjectOnPlane(_sourceAnimator.forward, Vector3.up).normalized;
            Facing = forward.sqrMagnitude > .001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            Origin = player.transform.position + Facing * Vector3.right * 6f;
            _root = UltimatePreviewModel.Create(player.Animator, out _model, out _animator);
            _root.name = "Attack Studio • disposable preview";
            _model.name = "Attack visual model";
            _animator.speed = 1;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _animator.fireEvents = false;
            _root.transform.SetPositionAndRotation(Origin, Facing);
            var playerProxy = new GameObject("Attack preview / player attachments") { hideFlags = HideFlags.HideAndDontSave };
            _playerProxy = playerProxy.transform;
            _playerProxy.SetParent(_root.transform, false);
            _playerProxy.localRotation = Quaternion.Inverse(Facing) * player.transform.rotation;
            _playerProxy.localScale = player.transform.lossyScale;
            PairHierarchy(_sourceAnimator, _model.transform);
            if (_animator.isHuman)
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = player.Animator.GetBoneTransform((HumanBodyBones)i);
                    for (Transform t = bone; t != null && t != _sourceAnimator; t = t.parent) _animated.Add(t);
                }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                Transform target = string.IsNullOrEmpty(binding.path) ? _sourceAnimator : _sourceAnimator.Find(binding.path);
                if (target != null) _animated.Add(target);
            }
            _rootMotion = new UltimateSequenceClip { clip = clip, rootSamples = UltimateRootMotionBaker.Extract(clip, player.Animator) };
            _graph = PlayableGraph.Create("Attack Studio animation preview");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _playable = AnimationClipPlayable.Create(_graph, clip);
            _playable.SetSpeed(0);
            _playable.SetApplyFootIK(false);
            _playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(_graph, "Attack pose", _animator).SetSourcePlayable(_playable);
            _graph.Play();
            BuildEffects(director);
            Sample(0);
        }
        catch { Dispose(); throw; }
    }

    private void PairHierarchy(Transform source, Transform clone)
    {
        _transforms[source] = clone;
        for (int i = 0; i < source.childCount && i < clone.childCount; i++)
            PairHierarchy(source.GetChild(i), clone.GetChild(i));
    }

    /// <summary>Maps an authored collider/attachment to the sampled pose, without enabling a collider.</summary>
    public Transform ResolveTransform(Transform source)
    {
        if (source == null || _root == null || _player == null) return null;
        if (source == _player.transform) return _playerProxy;
        if (_transforms.TryGetValue(source, out Transform clone))
        {
            if (_external.Contains(source)) CopyExternal(source, clone);
            else if (source != _sourceAnimator && !_animated.Contains(source))
            {
                if (source.parent != _sourceAnimator) ResolveTransform(source.parent);
                CopyLocal(source, clone);
            }
            return clone;
        }
        // Non-model hitboxes and scene effects receive inert transform proxies, never copied behaviours.
        var proxy = new GameObject("Attack preview attachment / " + source.name) { hideFlags = HideFlags.HideAndDontSave };
        Transform parent = source.IsChildOf(_player.transform) ? ResolveTransform(source.parent) : _root.transform;
        proxy.transform.SetParent(parent, false);
        if (source.IsChildOf(_player.transform)) CopyLocal(source, proxy.transform);
        else
        {
            _external.Add(source);
            CopyExternal(source, proxy.transform);
        }
        _transforms[source] = proxy.transform;
        return proxy.transform;
    }

    private void CopyExternal(Transform source, Transform target)
    {
        target.localPosition = Quaternion.Inverse(Facing) * (source.position - _player.transform.position);
        target.localRotation = Quaternion.Inverse(Facing) * source.rotation;
        target.localScale = source.lossyScale;
    }

    private static void CopyLocal(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private void BuildEffects(PlayableDirector director)
    {
        if (director == null || director.playableAsset is not TimelineAsset timeline) return;
        foreach (TrackAsset track in PlayerSkillStudio.AllTracks(timeline))
        {
            if (track.mutedInHierarchy) continue;
            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.asset is not ControlPlayableAsset control) continue;
                GameObject source = control.sourceGameObject.Resolve(director);
                bool prefab = control.prefabGameObject != null;
                GameObject template = prefab ? control.prefabGameObject : source;
                if (template == null) continue;
                var staging = new GameObject("Attack effect staging") { hideFlags = HideFlags.HideAndDontSave };
                staging.transform.SetParent(_root.transform, false);
                staging.SetActive(false);
                GameObject clone = null;
                try
                {
                    clone = Object.Instantiate(template, staging.transform, false);
                    clone.name = "Attack preview / " + template.name;
                    StripEffectBehaviours(clone);
                    Transform parent = prefab ? (source != null ? ResolveTransform(source.transform) : _root.transform)
                        : source.transform.parent != null ? ResolveTransform(source.transform.parent) : _root.transform;
                    clone.SetActive(false);
                    clone.transform.SetParent(parent, false);
                    if (!prefab && source != null) CopyLocal(source.transform, clone.transform);
                    var particles = clone.GetComponentsInChildren<ParticleSystem>(true);
                    uint seed = control.particleRandomSeed == 0 ? 1u : control.particleRandomSeed;
                    foreach (var particle in particles)
                    {
                        particle.useAutoRandomSeed = false;
                        particle.randomSeed = seed++;
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = particle.main;
                        main.stopAction = ParticleSystemStopAction.None;
                        if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null &&
                            !main.customSimulationSpace.IsChildOf(clone.transform))
                            main.customSimulationSpace = ResolveTransform(main.customSimulationSpace);
                        var renderer = particle.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null) renderer.enabled = true;
                    }
                    _effects.Add(new Effect
                    {
                        Clip = clip, Track = track, Control = control, Clone = clone,
                        Source = source != null ? source.transform : null, IsPrefab = prefab,
                        Roots = particles.Where(p => !HasParticleAncestor(p.transform, clone.transform)).ToArray()
                    });
                }
                catch { if (clone != null) Object.DestroyImmediate(clone); throw; }
                finally { Object.DestroyImmediate(staging); }
            }
        }
    }

    private static bool HasParticleAncestor(Transform transform, Transform root)
    {
        if (transform == root) return false;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
        {
            if (parent.GetComponent<ParticleSystem>() != null) return true;
            if (parent == root) break;
        }
        return false;
    }

    private static void StripEffectBehaviours(GameObject clone)
    {
        foreach (var item in clone.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<PlayableDirector>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(item);
        foreach (var item in clone.GetComponentsInChildren<Transform>(true)) item.gameObject.hideFlags = HideFlags.HideAndDontSave;
    }

    public void Sample(double seconds)
    {
        if (!IsReady) return;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
        SampledTime = Math.Max(0, seconds);
        float sourceTime = Mathf.Clamp((float)(SampledTime * _speed), 0, _clip.length);
        _playable.SetTime(sourceTime);
        _graph.Evaluate(0);
        Vector3 motion = UltimateSequenceUtility.SampleRoot(_rootMotion, sourceTime).position;
        // Basic/RunAttack runtime projects Animator deltas onto the locked facing direction.
        _root.transform.SetPositionAndRotation(Origin + Facing * Vector3.forward * motion.z, Facing);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
        foreach (var effect in _effects)
        {
            if (effect.Clone == null) continue;
            bool active = !effect.Track.mutedInHierarchy && SampledTime >= effect.Clip.start && SampledTime < effect.Clip.end;
            if (!effect.IsPrefab && effect.Source != null) CopyLocal(effect.Source, effect.Clone.transform);
            if (effect.Source != null) ResolveTransform(effect.IsPrefab ? effect.Source : effect.Source.parent);
            effect.Clone.SetActive(active);
            if (!active) continue;
            float elapsed = (float)Math.Max(0, effect.Clip.clipIn + (SampledTime - effect.Clip.start) * effect.Clip.timeScale);
            foreach (var particle in effect.Roots)
                if (particle != null) particle.Simulate(elapsed, true, true, true);
        }
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        if (_root != null) Object.DestroyImmediate(_root);
        _root = null; _model = null; _playerProxy = null; _animator = null; _player = null; _clip = null;
        _sourceAnimator = null; _rootMotion = null;
        _transforms.Clear(); _animated.Clear(); _external.Clear(); _effects.Clear();
    }
}
