using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

// Keeps the vendor's particle curves/shader, with time owned by the skill Timeline.
[ExecuteAlways, DefaultExecutionOrder(10000), DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
public sealed class PlayerSkillImpactFrame : TimelineActivationTarget
{
    [SerializeField] private Transform _player;
    [SerializeField] private ParticleSystem _effect;
    [SerializeField] private TimelineActivationTrack _track;
    [SerializeField, Tooltip("플레이어 기준 임팩트 중심 위치입니다.")]
    private Vector3 _centerOffset = Vector3.up;
    [SerializeField, Min(0.001f), Tooltip("원본 파티클 애니메이션 길이. 타임라인 클립 길이에 맞춰 재생합니다.")]
    private float _sourceDuration = 0.3f;
    [SerializeField, Range(1f, 12f), Tooltip("화면 높이를 기준으로 한 중심 링 크기입니다. 화면을 덮는 크기와 독립적으로 조절합니다.")]
    private float _coreScreenScale = 9f;
    [SerializeField, Range(1f, 1.5f), Tooltip("현재 캐릭터와 무기 주변의 왜곡 보호 여유입니다.")]
    private float _playerProtectionPadding = 1.1f;
    [SerializeField, Tooltip("임팩트 렌더 중에만 플레이어의 표면 명암을 분리하여 왜곡과 중심 링으로부터 보호합니다.")]
    private bool _emphasizePlayerSilhouette = true;
    [SerializeField, Tooltip("플레이어의 명암과 픽셀 마스크를 임팩트 셰이더에 전달하는 기준 재질입니다.")]
    private Material _silhouetteMaterial;

    private PlayableDirector _director;
    private float _sampledTime = -1f;
    private readonly Dictionary<Material, Material> _silhouetteMaterials = new();
    private readonly List<(Renderer renderer, Material[] originals)> _renderMaterials = new();
    private Camera _silhouetteCamera;
    private MaterialPropertyBlock _impactProperties;
    private static readonly int CoreUVScale = Shader.PropertyToID("_CoreUVScale");
    private static readonly int PlayerProtection = Shader.PropertyToID("_PlayerProtection");
    private static readonly int EffectTime = Shader.PropertyToID("_EffectTime");
    private object _externalOwner;
    private object _externalActivationOwner;
    private float _externalNormalizedTime;
    private Vector3 _externalPosition, _externalScale;
    private Transform _accents;
    private Vector3 _externalAccentScale;
    private Quaternion _externalRotation;

    public ParticleSystem Effect => _effect;
    public Transform Player => _player;
    public Vector3 Center => _player != null ? _player.TransformPoint(_centerOffset) : transform.position;

    protected override Object Resource => _effect != null && _player != null
        && !transform.IsChildOf(_effect.transform) ? _effect.gameObject : null;
    protected override bool ReadActive(Object resource) => ((GameObject)resource).activeSelf;

    protected override void WriteActive(Object resource, bool active)
    {
        ((GameObject)resource).SetActive(active);
        _sampledTime = -1f;
        if (active)
            Sample();
        else if (_effect != null)
        {
            _effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ReleaseSilhouetteMaterials();
        }
    }

    public void Configure(Transform player, ParticleSystem effect, TimelineActivationTrack track, Material silhouetteMaterial = null)
    {
        ReleaseSilhouetteMaterials();
        base.OnValidate();
        _player = player;
        _effect = effect;
        _accents = _effect != null ? _effect.transform.Find("Impact Accents") : null;
        _track = track;
        _silhouetteMaterial = silhouetteMaterial;
        _director = GetComponent<PlayableDirector>();
        _sampledTime = -1f;
    }

    /// <summary>Claims the existing effect without requiring a Timeline clip or clock.</summary>
    public void BeginExternalSession(object owner)
    {
        if (owner == null || ReferenceEquals(_externalOwner, owner)) return;
        if (_externalOwner != null) EndExternalSession(_externalOwner);
        _externalOwner = owner;
        _externalActivationOwner = new object();
        if (_effect != null)
        {
            if (_accents == null) _accents = _effect.transform.Find("Impact Accents");
            _externalPosition = _effect.transform.localPosition;
            _externalRotation = _effect.transform.localRotation;
            _externalScale = _effect.transform.localScale;
            if (_accents != null) _externalAccentScale = _accents.localScale;
        }
        _externalNormalizedTime = 0f;
        RegisterOwner(_externalActivationOwner);
    }

    /// <summary>Normalized absolute effect time; shared by runtime and Studio preview.</summary>
    public void SampleExternal(object owner, bool active, float normalizedTime)
    {
        if (owner == null) return;
        BeginExternalSession(owner);
        if (!ReferenceEquals(_externalOwner, owner)) return;
        _externalNormalizedTime = Mathf.Clamp01(normalizedTime);
        SetRequestedActive(_externalActivationOwner, active);
        if (active) Sample();
    }

    public void EndExternalSession(object owner)
    {
        if (owner == null || !ReferenceEquals(_externalOwner, owner)) return;
        ReleaseOwner(_externalActivationOwner);
        ReleaseSilhouetteMaterials();
        if (_effect != null)
        {
            _effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _effect.transform.SetLocalPositionAndRotation(_externalPosition, _externalRotation);
            _effect.transform.localScale = _externalScale;
            if (_accents != null) _accents.localScale = _externalAccentScale;
        }
        _externalOwner = _externalActivationOwner = null; _sampledTime = -1f;
    }

    private void OnEnable()
    {
        _director = GetComponent<PlayableDirector>();
        _accents = _effect != null ? _effect.transform.Find("Impact Accents") : null;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
        if (Application.isPlaying && _effect != null)
            CombatVfxTime.RegisterHierarchy(_effect.gameObject, timelineControlsParticle: true);
    }

    protected override void OnDisable()
    {
        if (_externalOwner != null) EndExternalSession(_externalOwner);
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        ReleaseSilhouetteMaterials();
        base.OnDisable();
        _sampledTime = -1f;
    }

    private void LateUpdate() => Sample();

    // Called after Cinemachine has positioned the rendering camera, including Timeline preview.
    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (_effect == null || !_effect.gameObject.activeInHierarchy ||
            (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            return;
        Sample();
        PrepareForCamera(camera);
        PrepareSilhouette(camera);
    }

    // Encode shaded player pixels in the opaque texture. The effect decodes them
    // into monochrome, so the ring can wrap around the exact pose and weapon.
    // Swap only during a camera render so Timeline's opacity materials always
    // see the real originals, including on a frame that jumps into the dash.
    private void PrepareSilhouette(Camera camera)
    {
        RestoreRenderMaterials();
        // Release the bright input during the asset's final fade, so its
        // transparent tail reveals the normal costume instead of glowing white.
        if (!_emphasizePlayerSilhouette || _silhouetteMaterial == null || _player == null || _sampledTime >= _sourceDuration * 0.8f)
            return;
        var player = _player.GetComponent<PlayerCore>();
        Transform model = player != null && player.Animator != null ? player.Animator.transform : _player;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer))
                continue;
            Material[] originals = renderer.sharedMaterials;
            var silhouettes = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                if (source == null)
                    continue;
                if (!_silhouetteMaterials.TryGetValue(source, out Material material))
                {
                    material = new Material(_silhouetteMaterial)
                    {
                        name = source.name + " (Impact Silhouette)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    material.CopyPropertiesFromMaterial(source);
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", LiftAlbedo(source.GetColor("_BaseColor")));
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", LiftAlbedo(source.GetColor("_Color")));
                    _silhouetteMaterials.Add(source, material);
                }
                silhouettes[i] = material;
            }
            _renderMaterials.Add((renderer, originals));
            renderer.sharedMaterials = silhouettes;
        }
        _silhouetteCamera = camera;
    }

    private static Color LiftAlbedo(Color color)
        => new(Mathf.Min(1f, color.r * 2f), Mathf.Min(1f, color.g * 2f),
            Mathf.Min(1f, color.b * 2f), color.a);

    private void AfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == _silhouetteCamera)
            RestoreRenderMaterials();
    }

    private void RestoreRenderMaterials()
    {
        foreach (var entry in _renderMaterials)
            if (entry.renderer != null)
                entry.renderer.sharedMaterials = entry.originals;
        _renderMaterials.Clear();
        _silhouetteCamera = null;
    }

    private void ReleaseSilhouetteMaterials()
    {
        RestoreRenderMaterials();
        foreach (Material material in _silhouetteMaterials.Values)
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        _silhouetteMaterials.Clear();
    }

    public void PrepareForCamera(Camera camera)
    {
        if (_effect == null || _player == null || camera == null)
            return;
        _impactProperties ??= new MaterialPropertyBlock();

        _effect.transform.SetPositionAndRotation(Center, Quaternion.identity);
        Vector3 viewport = camera.WorldToViewportPoint(Center);
        float height = camera.orthographic ? 2f * camera.orthographicSize
            : 2f * Mathf.Max(camera.nearClipPlane, viewport.z) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float farX = Mathf.Max(Mathf.Abs(viewport.x), Mathf.Abs(1f - viewport.x)) * height * camera.aspect;
        float farY = Mathf.Max(Mathf.Abs(viewport.y), Mathf.Abs(1f - viewport.y)) * height;
        // Keep every corner inside the opaque part of the source's soft radial
        // mask, rather than merely inside its geometry. Core sizing is independent.
        float scale = 3.3f * Mathf.Sqrt(farX * farX + farY * farY) / 100f;
        Vector3 parentScale = _effect.transform.parent != null ? _effect.transform.parent.lossyScale : Vector3.one;
        _effect.transform.localScale = new Vector3(scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)), scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        PrepareAccents();

        Vector4 protection = ProjectPlayerProtection(camera);
        foreach (var renderer in _effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(CoreUVScale)) continue;
            renderer.GetPropertyBlock(_impactProperties);
            _impactProperties.SetFloat(CoreUVScale, 100f * scale / Mathf.Max(.001f, height * _coreScreenScale));
            _impactProperties.SetVector(PlayerProtection, protection);
            _impactProperties.SetFloat(EffectTime, Mathf.Max(0f, _sampledTime));
            renderer.SetPropertyBlock(_impactProperties);
        }
    }

    private void PrepareAccents()
    {
        if (_accents == null && _effect != null) _accents = _effect.transform.Find("Impact Accents");
        if (_accents == null) return;
        // Local sparks retain their authored world size while the screen billboard fits the camera.
        Vector3 parentScale = _accents.parent.lossyScale;
        _accents.localScale = new Vector3(.75f / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
            .75f / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)), .75f / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
    }

    private Vector4 ProjectPlayerProtection(Camera camera)
    {
        var player = _player.GetComponent<PlayerCore>();
        Transform model = player != null && player.Animator != null ? player.Animator.transform : _player;
        Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)) continue;
            Bounds bounds = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = renderer.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                Vector3 point = camera.WorldToViewportPoint(corner);
                if (point.z <= camera.nearClipPlane) continue;
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
        }
        if (float.IsInfinity(min.x))
        {
            Vector3 center = camera.WorldToViewportPoint(Center);
            return new Vector4(center.x, center.y, .06f / camera.aspect, .1f);
        }
        Vector2 midpoint = (min + max) * .5f;
        Vector2 radius = (max - min) * (.5f * _playerProtectionPadding);
        return new Vector4(midpoint.x, midpoint.y, Mathf.Max(.01f, radius.x), Mathf.Max(.01f, radius.y));
    }

    private void Sample()
    {
        if (_effect == null || !_effect.gameObject.activeInHierarchy)
            return;

        _effect.transform.position = Center;
        PrepareAccents();
        if (_externalOwner != null)
        {
            float externalTime = Mathf.Max(.0001f, _externalNormalizedTime * Mathf.Max(.001f, _sourceDuration));
            if (!Mathf.Approximately(externalTime, _sampledTime))
            {
                _effect.Simulate(externalTime, true, true, false);
                _sampledTime = externalTime;
            }
            return;
        }
        if (_director == null || _track == null) return;
        foreach (TimelineClip clip in _track.GetClips())
        {
            if (_director.time < clip.start || _director.time >= clip.end)
                continue;
            float time = Mathf.Max(0.0001f, (float)((_director.time - clip.start) / clip.duration) * _sourceDuration);
            if (Mathf.Approximately(time, _sampledTime))
                return;
            // Restart at an absolute time: scrubbing, replay, pause and hit stop all
            // show the same particle frame without an independent realtime clock.
            _effect.Simulate(time, true, true, false);
            _sampledTime = time;
            return;
        }
    }

    public override void GatherProperties(IPropertyCollector collector)
    {
        if (_effect != null)
        {
            collector.AddFromName(_effect.gameObject, "m_IsActive");
            collector.AddFromComponent(_effect.gameObject, _effect.transform);
            if (_accents != null) collector.AddFromComponent(_accents.gameObject, _accents);
        }
    }
}
