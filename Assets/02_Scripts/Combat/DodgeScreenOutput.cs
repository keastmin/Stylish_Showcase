using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Owns a temporary volume; camera offsets are restored immediately after rendering.</summary>
public sealed class DodgeScreenOutput : IDisposable
{
    private readonly GameObject _root;
    private readonly Volume _volume;
    private readonly VolumeProfile _profile;
    private readonly Vignette _vignette;
    private readonly ColorAdjustments _color;
    private readonly ChromaticAberration _chromatic;
    private readonly LensDistortion _distortion;
    private Camera _camera;
    private float _fov, _shake, _clock, _savedFov;
    private Vector3 _savedPosition;
    private Quaternion _savedRotation;
    private bool _cameraModified;

    public DodgeScreenOutput(Camera camera, Transform parent = null, int layer = 0)
    {
        _camera = camera;
        _root = new GameObject("Dodge Studio • screen effects") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
        if (parent != null) _root.transform.SetParent(parent, false);
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.hideFlags = HideFlags.HideAndDontSave;
        _vignette = _profile.Add<Vignette>(false);
        _color = _profile.Add<ColorAdjustments>(false);
        _chromatic = _profile.Add<ChromaticAberration>(false);
        _distortion = _profile.Add<LensDistortion>(false);
        _volume = _root.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 1000;
        _volume.weight = 0;
        _volume.sharedProfile = _profile;
        RenderPipelineManager.beginCameraRendering += BeginRender;
        RenderPipelineManager.endCameraRendering += EndRender;
    }

    public void Set(DodgeSequence sequence, Camera camera = null)
    {
        if (camera != null) _camera = camera;
        Func<DodgeEffectKind, DodgeEffectValue> get = kind => sequence != null ? sequence.Effect(kind) : default;
        var vignette = get(DodgeEffectKind.Vignette);
        _vignette.active = vignette.active;
        _vignette.intensity.Override(Mathf.Clamp01(vignette.value));
        _vignette.color.Override(vignette.color);
        var saturation = get(DodgeEffectKind.Saturation);
        var exposure = get(DodgeEffectKind.Exposure);
        _color.active = saturation.active || exposure.active;
        _color.saturation.overrideState = saturation.active;
        _color.saturation.value = Mathf.Clamp(saturation.value, -100, 100);
        _color.postExposure.overrideState = exposure.active;
        _color.postExposure.value = exposure.value;
        var chromatic = get(DodgeEffectKind.ChromaticAberration);
        _chromatic.active = chromatic.active;
        _chromatic.intensity.Override(Mathf.Clamp01(chromatic.value));
        var distortion = get(DodgeEffectKind.LensDistortion);
        _distortion.active = distortion.active;
        _distortion.intensity.Override(Mathf.Clamp(distortion.value, -1, 1));
        _volume.weight = vignette.active || _color.active || chromatic.active || distortion.active ? 1 : 0;
        _fov = get(DodgeEffectKind.FieldOfView).value;
        _shake = get(DodgeEffectKind.CameraShake).value;
        _clock = sequence != null ? sequence.Time : 0;
    }

    public void SetVisible(bool visible) => _volume.enabled = visible;

    private void BeginRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _camera || _cameraModified || Mathf.Abs(_fov) + Mathf.Abs(_shake) < .00001f) return;
        _savedFov = camera.fieldOfView;
        _savedPosition = camera.transform.position;
        _savedRotation = camera.transform.rotation;
        _cameraModified = true;
        camera.fieldOfView = Mathf.Clamp(_savedFov + _fov, 5, 150);
        camera.transform.rotation = _savedRotation * Quaternion.Euler(Mathf.Sin(_clock * 107) * _shake, Mathf.Sin(_clock * 83 + 1) * _shake, Mathf.Sin(_clock * 67) * _shake * .4f);
    }

    private void EndRender(ScriptableRenderContext context, Camera camera) { if (camera == _camera) RestoreCamera(); }
    private void RestoreCamera()
    {
        if (!_cameraModified || _camera == null) return;
        _camera.fieldOfView = _savedFov;
        _camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
        _cameraModified = false;
    }

    public void Dispose()
    {
        _volume.enabled = false;
        RenderPipelineManager.beginCameraRendering -= BeginRender;
        RenderPipelineManager.endCameraRendering -= EndRender;
        RestoreCamera();
        foreach (var component in _profile.components)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(component);
            else UnityEngine.Object.DestroyImmediate(component);
        }
        _profile.components.Clear();
        if (Application.isPlaying) { UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_profile); }
        else { UnityEngine.Object.DestroyImmediate(_root); UnityEngine.Object.DestroyImmediate(_profile); }
    }
}

