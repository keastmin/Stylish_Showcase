using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Shared by the isolated editor preview and runtime render stage.</summary>
public sealed class VirtualCutsceneStage : IDisposable
{
    private readonly VirtualCutsceneDefinition _definition;
    private readonly List<GameObject> _effects = new();
    private readonly List<ParticleSystem[]> _effectParticles = new();
    private readonly List<ParticleSystem> _backgroundParticles = new();
    private readonly List<Material> _materials = new();
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private AnimationClipPlayable[] _clips;
    private Animator _animator;
    private Transform _model;
    private Vector3 _modelPosition, _modelScale;
    private Quaternion _modelRotation = Quaternion.identity;
    public GameObject Root { get; private set; }
    public Camera Camera { get; }
    public Animator Animator => _animator;
    public VirtualCutsceneDefinition Definition => _definition;

    public VirtualCutsceneStage(VirtualCutsceneDefinition definition, Animator source, Camera camera)
    {
        _definition = definition != null ? definition : throw new ArgumentNullException(nameof(definition));
        Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
        try { Build(source); }
        catch { Dispose(); throw; }
    }

    private void Build(Animator source)
    {
        Root = new GameObject("Virtual Cutscene • visual stage") { hideFlags = HideFlags.HideAndDontSave };
        Root.SetActive(false);
        var actorSource = _definition.actorPrefab != null ? _definition.actorPrefab : source != null ? source.gameObject : null;
        if (actorSource != null)
        {
            var actor = CloneVisual(actorSource, Root.transform);
            actor.name = "Cutscene Actor (visual only)";
            _animator = actor.GetComponentInChildren<Animator>(true);
            if (_animator != null)
            {
                _animator.runtimeAnimatorController = null;
                _animator.applyRootMotion = false;
                _animator.fireEvents = false;
                _animator.speed = 1;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.enabled = true;
            }
            _model = actor.transform;
            _modelPosition = _definition.actorPosition;
            _modelRotation = Quaternion.Euler(_definition.actorEuler);
            _modelScale = (_definition.actorPrefab != null ? actorSource.transform.localScale : source.transform.lossyScale) * _definition.actorScale;
            foreach (var particles in actor.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var trail in actor.GetComponentsInChildren<TrailRenderer>(true)) trail.enabled = false;
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.forceMatrixRecalculationPerRender = true;
            }
            BuildAnimation();
        }
        if (_definition.backgroundPrefab != null)
        {
            var background = CloneVisual(_definition.backgroundPrefab, Root.transform);
            background.transform.SetLocalPositionAndRotation(_definition.backgroundPosition, Quaternion.Euler(_definition.backgroundEuler));
            background.transform.localScale = _definition.backgroundScale;
            _backgroundParticles.AddRange(background.GetComponentsInChildren<ParticleSystem>(true));
        }
        if (_definition.showFloor)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Studio Floor";
            floor.transform.SetParent(Root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.06f, 0);
            floor.transform.localScale = new Vector3(80, .1f, 80);
            Remove(floor.GetComponent<Collider>());
            if (_definition.floorMaterial != null) floor.GetComponent<Renderer>().sharedMaterial = _definition.floorMaterial;
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = _definition.floorColor };
                    _materials.Add(material);
                    floor.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
        }
        CreateLight("Studio Key", _definition.keyLightEuler, _definition.keyLightColor, _definition.keyLightIntensity);
        CreateLight("Studio Fill", new Vector3(25, 145, 0), _definition.fillLightColor, _definition.fillLightIntensity);
        if (_definition.effects != null)
            foreach (var entry in _definition.effects)
            {
                GameObject effect = entry?.prefab != null ? CloneVisual(entry.prefab, Root.transform) : null;
                if (effect != null)
                {
                    effect.transform.localPosition = entry.relativeToActor ? _definition.actorPosition + _modelRotation * entry.position : entry.position;
                    effect.transform.localRotation = (entry.relativeToActor ? _modelRotation : Quaternion.identity) * Quaternion.Euler(entry.euler);
                    effect.transform.localScale = entry.scale;
                }
                _effects.Add(effect);
                _effectParticles.Add(effect != null ? effect.GetComponentsInChildren<ParticleSystem>(true) : Array.Empty<ParticleSystem>());
            }
        foreach (var transform in Root.GetComponentsInChildren<Transform>(true))
        {
            transform.gameObject.layer = _definition.renderLayer;
            transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }
        foreach (var particles in Root.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 713;
            var main = particles.main;
            main.playOnAwake = false;
            main.useUnscaledTime = false;
        }
        // In a player, Destroy(component) is deferred. Keep the clone inactive until
        // every copied script is gone, including disabled scripts whose Awake would
        // otherwise execute when the hierarchy becomes active.
        Root.SetActive(!Application.isPlaying);
        Camera.clearFlags = _definition.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        Camera.backgroundColor = _definition.backgroundColor;
        Camera.nearClipPlane = .03f;
        Camera.farClipPlane = 200;
        Camera.cullingMask = 1 << _definition.renderLayer;
        var skybox = Camera.GetComponent<Skybox>();
        if (skybox == null) skybox = Camera.gameObject.AddComponent<Skybox>();
        skybox.material = _definition.skybox;
        skybox.enabled = _definition.skybox != null;
        var data = Camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.volumeLayerMask = 1 << _definition.renderLayer;
        Sample(0);
    }

    public bool TryActivate()
    {
        if (Root == null) return false;
        if (Root.activeSelf) return true;
        if (Root.GetComponentsInChildren<MonoBehaviour>(true).Length > 0) return false;
        Root.SetActive(true);
        return true;
    }

    private void CreateLight(string name, Vector3 euler, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Root.transform, false);
        go.transform.localRotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << _definition.renderLayer;
        light.shadows = LightShadows.None;
    }

    private void BuildAnimation()
    {
        int count = _definition.animations?.Count ?? 0;
        if (count == 0) return;
        _graph = PlayableGraph.Create("Virtual Cutscene • manual animation");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _mixer = AnimationMixerPlayable.Create(_graph, count);
        _clips = new AnimationClipPlayable[count];
        for (int i = 0; i < count; i++)
        {
            var entry = _definition.animations[i];
            if (entry?.clip == null) continue;
            _clips[i] = AnimationClipPlayable.Create(_graph, entry.clip);
            _clips[i].SetApplyFootIK(false);
            _clips[i].SetApplyPlayableIK(false);
            _clips[i].SetSpeed(0);
            _graph.Connect(_clips[i], 0, _mixer, i);
        }
        var output = AnimationPlayableOutput.Create(_graph, "Studio actor", _animator);
        output.SetSourcePlayable(_mixer);
        _graph.Play();
    }

    public void Sample(float time)
    {
        time = Mathf.Clamp(time, 0, _definition.Duration);
        if (_graph.IsValid())
        {
            int selected = -1;
            for (int i = 0; i < _clips.Length; i++)
            {
                _mixer.SetInputWeight(i, 0);
                var entry = _definition.animations[i];
                if (_clips[i].IsValid() && entry.start <= time &&
                    (selected < 0 || entry.start >= _definition.animations[selected].start)) selected = i;
            }
            // Before the first segment, hold its first frame. Gaps hold the preceding final pose.
            if (selected < 0)
                for (int i = 0; i < _clips.Length; i++)
                    if (_clips[i].IsValid() && (selected < 0 || _definition.animations[i].start < _definition.animations[selected].start)) selected = i;
            if (selected >= 0)
            {
                _clips[selected].SetTime(_definition.animations[selected].SourceTime(time));
                _mixer.SetInputWeight(selected, 1);
                _graph.Evaluate(0);
            }
        }
        if (_model != null)
        {
            _model.SetLocalPositionAndRotation(_modelPosition, _modelRotation);
            _model.localScale = _modelScale;
        }
        for (int i = 0; i < _effects.Count; i++)
        {
            if (_effects[i] == null) continue;
            var entry = _definition.effects[i];
            bool active = time >= entry.start && time < entry.start + Mathf.Max(.01f, entry.duration);
            _effects[i].SetActive(active);
            if (active) Simulate(_effectParticles[i], time - entry.start);
        }
        Simulate(_backgroundParticles, time);
        var shot = _definition.ShotAt(time);
        if (shot != null)
        {
            shot.Evaluate(time, out var position, out var target, out var fov);
            Vector3 direction = target - position;
            Camera.transform.SetPositionAndRotation(Root.transform.TransformPoint(position),
                Root.transform.rotation * (direction.sqrMagnitude > .00001f ? Quaternion.LookRotation(direction) : Quaternion.identity) * Quaternion.Euler(0, 0, shot.roll));
            Camera.fieldOfView = fov;
        }
    }

    private static void Simulate(IEnumerable<ParticleSystem> systems, float time)
    {
        foreach (var system in systems)
        {
            if (system == null || !system.gameObject.activeInHierarchy) continue;
            system.Simulate(Mathf.Max(0, time), false, true, false);
            system.Pause(false);
        }
    }

    private static GameObject CloneVisual(GameObject source, Transform parent)
    {
        // The inactive parent prevents Awake/OnEnable on gameplay scripts in the clone.
        var clone = Object.Instantiate(source, parent, false);
        foreach (var script in clone.GetComponentsInChildren<MonoBehaviour>(true)) { script.enabled = false; Remove(script); }
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; Remove(collider); }
        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; Remove(body); }
        foreach (var audio in clone.GetComponentsInChildren<AudioSource>(true)) { audio.enabled = false; Remove(audio); }
        foreach (var listener in clone.GetComponentsInChildren<AudioListener>(true)) { listener.enabled = false; Remove(listener); }
        foreach (var camera in clone.GetComponentsInChildren<Camera>(true)) { camera.enabled = false; Remove(camera); }
        foreach (var director in clone.GetComponentsInChildren<PlayableDirector>(true)) { director.enabled = false; Remove(director); }
        foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
        {
            animator.fireEvents = false;
            animator.applyRootMotion = false;
            animator.enabled = false;
        }
        clone.SetActive(true);
        return clone;
    }

    public static void Remove(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        if (Root != null) { Root.SetActive(false); Remove(Root); }
        Root = null;
        foreach (var material in _materials) Remove(material);
        _materials.Clear();
    }
}
