using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>A disposable visual-only model. Never samples the scene player's Animator.</summary>
internal sealed class UltimateStudioPreview : System.IDisposable
{
    private GameObject _root;
    private GameObject _model;
    private PlayableGraph _graph;
    private AnimationClipPlayable _clipPlayable;
    private AnimationClip _clip;
    private PlayerUltimateDefinition _definition;
    private UltimatePosePlayer _sequencePlayer;
    public Vector3 Origin { get; private set; }
    public Quaternion Facing { get; private set; }
    public bool IsReady => _root != null;
    public bool IsSequence => _sequencePlayer != null;

    public void Build(PlayerCore player, AnimationClip clip)
    {
        Dispose();
        if (player == null || player.Animator == null || clip == null) return;
        var source = player.Animator;
        Facing = source.transform.rotation;
        Origin = player.transform.position + Facing * Vector3.right * 6f;
        _root = new GameObject("Ultimate Studio • disposable preview") { hideFlags = HideFlags.HideAndDontSave };
        // An inactive parent prevents copied behaviours from running while they are removed.
        _root.SetActive(false);
        _model = Object.Instantiate(source.gameObject, _root.transform, false);
        _model.name = "Ultimate visual model";
        foreach (var behaviour in _model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (var collider in _model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (var body in _model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (var sound in _model.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(sound);
        foreach (var listener in _model.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(listener);
        foreach (var camera in _model.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(camera);
        foreach (var light in _model.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
        foreach (var particles in _model.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = false;
        }
        foreach (var trail in _model.GetComponentsInChildren<TrailRenderer>(true)) trail.enabled = false;
        foreach (var line in _model.GetComponentsInChildren<LineRenderer>(true)) line.enabled = false;
        foreach (var transform in _root.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
        _model.transform.localScale = source.transform.lossyScale;
        var animator = _model.GetComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        _model.SetActive(true);
        _root.SetActive(true);
        _graph = PlayableGraph.Create("Ultimate Studio preview");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetApplyFootIK(false);
        _clipPlayable.SetApplyPlayableIK(false);
        var output = AnimationPlayableOutput.Create(_graph, "Visual only", animator);
        output.SetSourcePlayable(_clipPlayable);
        _graph.Play();
        _clip = clip;
        Sample(0f, Vector3.zero);
    }

    public bool UsesClip(AnimationClip clip) => _clip == clip;
    public bool UsesDefinition(PlayerUltimateDefinition definition) => _definition == definition && IsSequence;

    public void BuildSequence(PlayerCore player, PlayerUltimateDefinition definition)
    {
        Dispose();
        if (player == null || player.Animator == null || definition == null || !definition.HasSequence) return;
        var source = player.Animator;
        Facing = source.transform.rotation;
        Origin = player.transform.position + Facing * Vector3.right * 6f;
        _root = UltimatePreviewModel.Create(source, out _model, out var animator);
        _definition = definition;
        _sequencePlayer = UltimatePosePlayer.Create(animator, definition);
        SampleSequence(0);
    }

    public void SampleSequence(float time)
    {
        if (!IsReady || _sequencePlayer == null || _definition == null) return;
        _sequencePlayer.Sample(time);
        var motion = UltimateSequenceUtility.EvaluateMotion(_definition, time);
        _root.transform.SetPositionAndRotation(Origin + Facing * motion.position, Facing * motion.rotation);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
    }

    public void Sample(float time, Vector3 localOffset)
    {
        if (!IsReady || !_graph.IsValid()) return;
        _clipPlayable.SetTime(time);
        _graph.Evaluate(0f);
        // Runtime locks animation root motion and applies the same authored path offset.
        _root.transform.SetPositionAndRotation(Origin + Facing * localOffset, Facing);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
    }

    public void Dispose()
    {
        _sequencePlayer?.Dispose();
        _sequencePlayer = null;
        _definition = null;
        if (_graph.IsValid()) _graph.Destroy();
        if (_root != null) Object.DestroyImmediate(_root);
        _root = null;
        _model = null;
        _clip = null;
    }
}
