using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

internal sealed class DodgeStudioPreview : IDisposable
{
    private PreviewRenderUtility _render;
    private GameObject _holder, _model;
    private Animator _animator;
    private PlayableGraph _graph;
    private AnimationClipPlayable _playable;
    private AnimationClip _clip;
    private DodgeScreenOutput _screen;
    private Material _floorMaterial;
    private Material _targetMaterial, _rangeMaterial;
    private GameObject _target, _range;
    private Mesh _rangeMesh;
    private Texture _lastTexture;
    private DodgeAfterimagePreview _afterimage;
    public DodgeAfterimagePreview Afterimage => _afterimage;
    private readonly Dictionary<AnimationClip, UltimateSequenceClip> _roots = new Dictionary<AnimationClip, UltimateSequenceClip>();
    public bool Ready => _render != null && _animator != null && _holder != null;
    public float yaw = 150, pitch = 12, distance = 4.8f;
    public bool follow = true;
    public Vector3 CurrentPosition { get; private set; }
    public Animator ModelAnimator => _animator;
    public AnimationClip SampledClip => _clip;
    public float SampledTime { get; private set; }
    public int PoseSamples { get; private set; }

    public void Build(PlayerCore player)
    {
        Dispose();
        if (player == null || player.Animator == null) return;
        _render = new PreviewRenderUtility();
        // URP skips post-processing for CameraType.Preview. Geometry remains in
        // PreviewRenderUtility's isolated scene; Game enables the real URP effects.
        _render.camera.cameraType = CameraType.Game;
        _render.camera.fieldOfView = 42;
        _render.camera.nearClipPlane = .05f;
        _render.camera.farClipPlane = 150;
        _render.camera.backgroundColor = new Color(.085f, .1f, .13f);
        _render.camera.clearFlags = CameraClearFlags.SolidColor;
        var cameraData = _render.camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.volumeLayerMask = 1 << 31;
        _render.lights[0].intensity = 1.3f;
        _render.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
        _render.lights[1].intensity = .7f;
        _holder = UltimatePreviewModel.Create(player.Animator, out _model, out _animator);
        // A scene actor may currently be slowed or hit-stopped. The preview has
        // its own clock and must not inherit that actor's Animator.speed.
        _animator.speed = 1;
        _animator.updateMode = AnimatorUpdateMode.Normal;
        foreach (var skin in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // Manual Playable evaluation updates the Transform bones, but a Game
            // camera in a PreviewScene may reuse its previous GPU skin matrices.
            // Refresh the clone's deformation for every preview render as well.
            skin.updateWhenOffscreen = true;
            skin.forceMatrixRecalculationPerRender = true;
        }
        _holder.name = "Dodge Studio • disposable model";
        _holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _render.AddSingleGO(_holder);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Dodge preview ground";
        floor.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.transform.localScale = new Vector3(80, .05f, 80);
        floor.transform.position = new Vector3(0, -.05f, 0);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            _floorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _floorMaterial.color = new Color(.15f, .18f, .21f);
            floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
        }
        _render.AddSingleGO(floor);
        _target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        _target.name = "Dodge preview test target";
        _target.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DestroyImmediate(_target.GetComponent<Collider>());
        if (shader != null)
        {
            _targetMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _targetMaterial.color = new Color(.7f, .4f, .23f);
            _target.GetComponent<Renderer>().sharedMaterial = _targetMaterial;
        }
        _render.AddSingleGO(_target);
        _range = new GameObject("Dodge preview notice range") { hideFlags = HideFlags.HideAndDontSave };
        _rangeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        var points = new Vector3[128]; var triangles = new int[384];
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2 / 64;
            points[i * 2] = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            points[i * 2 + 1] = points[i * 2] * .985f;
            int next = (i + 1) % 64;
            int j = i * 6;
            triangles[j] = i * 2; triangles[j + 1] = next * 2; triangles[j + 2] = i * 2 + 1;
            triangles[j + 3] = i * 2 + 1; triangles[j + 4] = next * 2; triangles[j + 5] = next * 2 + 1;
        }
        _rangeMesh.vertices = points; _rangeMesh.triangles = triangles; _rangeMesh.RecalculateNormals();
        _range.AddComponent<MeshFilter>().sharedMesh = _rangeMesh;
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            _rangeMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            _rangeMaterial.color = GoldColor;
            _range.AddComponent<MeshRenderer>().sharedMaterial = _rangeMaterial;
        }
        _render.AddSingleGO(_range);
        _screen = new DodgeScreenOutput(_render.camera, _holder.transform, 31);
        _screen.SetVisible(false);
        _afterimage=new DodgeAfterimagePreview(_render,_model.transform,SamplePose);
    }

    private static Color GoldColor => new Color(1, .57f, .18f);
    public void SetThreat(bool visible, bool active, float distance, float radius)
    {
        if (_target == null) return;
        _target.SetActive(visible);
        _target.transform.position = new Vector3(0, 1, distance);
        _range.SetActive(visible && active);
        _range.transform.position = new Vector3(0, .015f, distance);
        _range.transform.localScale = Vector3.one * radius;
    }

    public Vector3 Root(DodgeMotion motion, float source, PlayerCore player)
    {
        if (motion.clip == null || player == null) return Vector3.zero;
        if (!_roots.TryGetValue(motion.clip, out var root))
        {
            root = new UltimateSequenceClip { clip = motion.clip, rootSamples = UltimateRootMotionBaker.Extract(motion.clip, _animator != null ? _animator : player.Animator) };
            _roots.Add(motion.clip, root);
        }
        return UltimateSequenceUtility.SampleRoot(root, source).position;
    }

    public void SamplePose(AnimationClip clip, float sourceTime, Vector3 position)
    {
        if (!Ready || clip == null) return;
        if (_clip != clip || !_graph.IsValid())
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graph = PlayableGraph.Create("Dodge Studio preview");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _playable = AnimationClipPlayable.Create(_graph, clip);
            _playable.SetSpeed(0);
            _playable.SetApplyFootIK(false);
            _playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(_graph, "Dodge model", _animator);
            output.SetSourcePlayable(_playable);
            _graph.Play();
            _clip = clip;
        }
        SampledTime = Mathf.Clamp(sourceTime, 0, clip.length);
        _playable.SetTime(SampledTime);
        _graph.Evaluate(0);
        PoseSamples++;
        CurrentPosition = position;
        _holder.transform.position = position;
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
    }

    public void Draw(Rect rect, DodgeSequence sequence, Vector3 position, DodgeMotion inspectionMotion = null, float inspectionTime = 0)
    {
        if (!Ready || sequence == null || rect.width < 10 || rect.height < 10) return;
        var clip = (inspectionMotion ?? sequence.Motion).clip;
        SamplePose(clip, inspectionMotion != null ? inspectionTime : sequence.SourceTime, position);
        // Layout, input and repaint each call OnGUI. Rendering on all of them
        // made the editor's playback clock crawl, especially with URP effects.
        if (Event.current.type != EventType.Repaint) return;
        _screen.Set(inspectionMotion == null ? sequence : null);
        Vector3 focus = (follow ? position : Vector3.zero) + Vector3.up;
        Quaternion view = Quaternion.Euler(pitch, yaw, 0);
        _render.camera.transform.SetPositionAndRotation(focus - view * Vector3.forward * distance, view);
        _render.BeginPreview(rect, GUIStyle.none);
        Texture texture;
        try
        {
            _screen.SetVisible(true);
            _afterimage?.Draw();
            _render.Render(true);
        }
        finally
        {
            _screen.SetVisible(false);
            texture = _render.EndPreview();
        }
        _lastTexture = texture;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
    }

    public void SaveLastFrame(string path)
    {
        if (_lastTexture == null || string.IsNullOrEmpty(path)) return;
        var buffer = RenderTexture.GetTemporary(_lastTexture.width, _lastTexture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            Graphics.Blit(_lastTexture, buffer);
            RenderTexture.active = buffer;
            image = new Texture2D(buffer.width, buffer.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, buffer.width, buffer.height), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
        }
    }

    public void Dispose()
    {
        _afterimage?.Dispose(); _afterimage=null;
        _screen?.Dispose(); _screen = null;
        if (_graph.IsValid()) _graph.Destroy();
        _render?.Cleanup(); _render = null;
        if (_floorMaterial != null) UnityEngine.Object.DestroyImmediate(_floorMaterial);
        if (_targetMaterial != null) UnityEngine.Object.DestroyImmediate(_targetMaterial);
        if (_rangeMaterial != null) UnityEngine.Object.DestroyImmediate(_rangeMaterial);
        if (_rangeMesh != null) UnityEngine.Object.DestroyImmediate(_rangeMesh);
        _holder = null; _model = null; _animator = null; _clip = null; _floorMaterial = null;
        _roots.Clear();
        _lastTexture = null;
        SampledTime = 0;
        _target = null; _range = null; _targetMaterial = null; _rangeMaterial = null; _rangeMesh = null;
    }
}
