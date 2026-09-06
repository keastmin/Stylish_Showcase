using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProjectileHitbox : MonoBehaviour
{
    [Header("Periodic Damage (Play Mode Only)")]
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField, Min(0f), Tooltip("적에게 피해를 줄 때마다 증가하는 스킬 게이지입니다.")]
    private float _skillGaugeAdditive = 0.2f;
    [SerializeField, Min(0.01f), Tooltip("전투 시간 기준 검사 간격입니다. 첫 검사는 생성 후 이 시간이 지난 뒤 실행됩니다.")]
    private float _damageInterval = 0.2f;
    [SerializeField, Min(0), Tooltip("피해를 받은 적만 이 값 + 1프레임 정지합니다. 0이면 히트스탑을 요청하지 않습니다.")]
    private int _hitStopFrame = 1;
    [SerializeField, Tooltip("적의 최소 요구 레벨 이상일 때만 피격 상태에 진입합니다. None도 피해와 히트스탑은 그대로 적용됩니다.")]
    private StaggerLevel _staggerLevel = StaggerLevel.None;
    [SerializeField] private LayerMask _targetLayers = 1 << 6; // Enemy Hurtbox

    [Header("Hitbox Shape and Preview")]
    [SerializeField] private bool _showPreview = true;
    [SerializeField, Tooltip("비워 두면 자신과 자식의 Mesh 파티클을 자동으로 사용합니다. Pivot과 왜곡용 Billboard는 제외합니다.")]
    private ParticleSystem[] _slashParticles = System.Array.Empty<ParticleSystem>();
    [SerializeField, Tooltip("자동 계산한 범위의 축별 크기 보정입니다. 투사체의 로컬 축을 기준으로 합니다.")]
    private Vector3 _sizeMultiplier = Vector3.one;
    [SerializeField, Tooltip("범위의 각 면에 추가하는 여유 공간입니다. 월드 단위이며 투사체의 로컬 축을 기준으로 합니다.")]
    private Vector3 _padding = new Vector3(0.05f, 0.05f, 0.05f);
    [SerializeField, Tooltip("납작한 Mesh에도 두께를 주기 위한 최소 크기입니다. 월드 단위입니다.")]
    private Vector3 _minimumSize = new Vector3(0.1f, 0.1f, 0.1f);
    [SerializeField] private Color _previewColor = new Color(0.2f, 1f, 0.3f, 1f);
    [SerializeField, Tooltip("각 파티클 시스템의 보정 전 범위를 하늘색으로 함께 표시합니다.")]
    private bool _showSourceBounds;

    private readonly List<ParticleSystem> _sources = new List<ParticleSystem>();
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Bounds> _sourceBounds = new List<Bounds>();
    private readonly HashSet<EnemyCore> _targetsInCurrentTick = new HashSet<EnemyCore>();
    private readonly List<IHitStopParticipant> _hitStopVictims = new List<IHitStopParticipant>();
    private Collider[] _overlapResults = new Collider[32];
    private Mesh _bakedMesh;
    private Camera _fallbackBakeCamera;
    private GameObject _owner;
    private PlayerCore _skillGaugeOwner;
    private PlayerSFXController _playerSFXController;
    private float _tickElapsed;
    private bool _refreshSources = true;

#if UNITY_EDITOR
    private bool _hadParticles;
    private double _nextRepaintTime;
#endif

    public void Initialize(GameObject owner)
    {
        _owner = owner;
        _skillGaugeOwner = owner != null ? owner.GetComponentInParent<PlayerCore>() : null;
        _playerSFXController = _skillGaugeOwner != null
            ? _skillGaugeOwner.GetComponentInChildren<PlayerSFXController>(true)
            : null;
        _tickElapsed = 0f;
        _targetsInCurrentTick.Clear();
        _hitStopVictims.Clear();
    }

    private void OnEnable()
    {
        _refreshSources = true;
        _tickElapsed = 0f;
        if (Application.IsPlaying(gameObject))
        {
            // 검사 시간과 VFX의 슬로모션 배율을 맞춥니다. 개별 히트스탑에는 연결하지 않습니다.
            CombatVfxTime.RegisterHierarchy(gameObject);
            foreach (ParticleSystem particle in GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= RefreshSceneView;
        UnityEditor.EditorApplication.update += RefreshSceneView;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= RefreshSceneView;
        _hadParticles = false;
        UnityEditor.SceneView.RepaintAll();
#endif
        ReleaseTemporaryObject(_bakedMesh);
        _bakedMesh = null;
        if (_fallbackBakeCamera != null)
            ReleaseTemporaryObject(_fallbackBakeCamera.gameObject);
        _fallbackBakeCamera = null;
        _owner = null;
        _skillGaugeOwner = null;
        _playerSFXController = null;
        _tickElapsed = 0f;
        _targetsInCurrentTick.Clear();
        _hitStopVictims.Clear();
        System.Array.Clear(_overlapResults, 0, _overlapResults.Length);
    }

    private void ReleaseTemporaryObject(Object temporaryObject)
    {
        if (temporaryObject == null)
            return;
        if (Application.IsPlaying(gameObject))
            Destroy(temporaryObject);
        else
            DestroyImmediate(temporaryObject);
    }

    private void LateUpdate()
    {
        // ExecuteAlways여도 편집 모드와 Prefab 모드에서는 피해를 주지 않습니다.
        if (!Application.IsPlaying(gameObject))
            return;

        TickDamage(CombatTimeController.DeltaTime);
    }

    private void TickDamage(float deltaTime)
    {
        if (deltaTime <= 0f || _damage <= 0f)
            return;

        float interval = Mathf.Max(0.01f, _damageInterval);
        _tickElapsed += deltaTime;
        if (_tickElapsed < interval)
            return;

        // 프레임이 지연돼도 현재 범위를 한 번만 검사해 같은 위치에서 피해가 몰리지 않게 합니다.
        _tickElapsed %= interval;
        if (!TryGetHitboxBounds(GetRuntimeBakeCamera(), out Bounds bounds, out Matrix4x4 boxToWorld))
            return;

        GiveDamage(boxToWorld.MultiplyPoint3x4(bounds.center), bounds.extents, transform.rotation);
    }

    private void GiveDamage(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        int count;
        // 버퍼가 가득 차면 확장 후 다시 검사해 밀집한 적을 누락하지 않습니다.
        while (true)
        {
            count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapResults, rotation,
                _targetLayers, QueryTriggerInteraction.Collide);
            if (count < _overlapResults.Length)
                break;
            System.Array.Resize(ref _overlapResults, _overlapResults.Length * 2);
        }

        _targetsInCurrentTick.Clear();
        _hitStopVictims.Clear();
        DamageData damageData = new DamageData(
            _owner != null ? _owner : gameObject,
            _damage,
            _hitStopFrame,
            _staggerLevel);
        bool dealtDamage = false;
        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapResults[i];
            _overlapResults[i] = null;
            if (hit == null)
                continue;

            // 레이어가 넓게 설정돼도 플레이어나 다른 IDamageable은 공격하지 않습니다.
            EnemyCore enemy = hit.GetComponentInParent<EnemyCore>();
            if (enemy == null || !enemy.isActiveAndEnabled || !_targetsInCurrentTick.Add(enemy))
                continue;
            if (!enemy.TryTakeDamage(damageData))
                continue;

            dealtDamage = true;
            _hitStopVictims.Add(enemy);
            if (_skillGaugeOwner != null && _skillGaugeAdditive > 0f)
                _skillGaugeOwner.AddSkillGauge(_skillGaugeAdditive);
        }

        // 한 검사에서 몇 명을 맞혀도 히트 피드백은 한 번만 재생합니다.
        if (dealtDamage)
            _playerSFXController?.PlayOneShotHitFeedbackSFX();

        HitstopCoordinator.RequestVictimsOnly(_hitStopVictims, _hitStopFrame);
    }

    private Camera GetRuntimeBakeCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera;

        // Mesh Bake API용입니다. 메인 카메라가 없어도 검사하도록 렌더링하지 않는 임시 카메라를 씁니다.
        if (_fallbackBakeCamera == null)
        {
            GameObject cameraObject = new GameObject("Projectile Hitbox Bake Camera (Temporary)")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _fallbackBakeCamera = cameraObject.AddComponent<Camera>();
            _fallbackBakeCamera.enabled = false;
        }
        return _fallbackBakeCamera;
    }

    private void OnValidate()
    {
        _damage = Mathf.Max(0f, _damage);
        _damageInterval = Mathf.Max(0.01f, _damageInterval);
        _hitStopFrame = Mathf.Max(0, _hitStopFrame);
        _sizeMultiplier = Vector3.Max(_sizeMultiplier, Vector3.zero);
        _padding = Vector3.Max(_padding, Vector3.zero);
        _minimumSize = Vector3.Max(_minimumSize, Vector3.one * 0.001f);
        _refreshSources = true;
    }

    private void OnTransformChildrenChanged() => _refreshSources = true;

#if UNITY_EDITOR
    [ContextMenu("Refresh Preview Sources")]
    private void RefreshPreviewSources()
    {
        _refreshSources = true;
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    private void CollectSources()
    {
        if (!_refreshSources)
            return;

        _refreshSources = false;
        _sources.Clear();
        ParticleSystem[] candidates = _slashParticles != null && _slashParticles.Length > 0
            ? _slashParticles
            : GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem particle in candidates)
        {
            if (particle != null && !_sources.Contains(particle)
                && particle.TryGetComponent(out ParticleSystemRenderer particleRenderer)
                && particleRenderer.renderMode == ParticleSystemRenderMode.Mesh)
            {
                _sources.Add(particle);
            }
        }
    }

    private bool TryGetHitboxBounds(Camera bakeCamera, out Bounds bounds, out Matrix4x4 boxToWorld)
    {
        CollectSources();
        _sourceBounds.Clear();
        // Mesh에 이미 월드 스케일이 적용돼 있습니다. 루트의 회전축을 기준으로 범위를 합칩니다.
        boxToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Matrix4x4 worldToBox = boxToWorld.inverse;
        bounds = default;
        bool hasBounds = false;
        foreach (ParticleSystem particle in _sources)
        {
            if (!TryGetSlashBounds(particle, bakeCamera, worldToBox, out Bounds sourceBounds))
                continue;

            _sourceBounds.Add(sourceBounds);
            if (hasBounds)
                bounds.Encapsulate(sourceBounds);
            else
            {
                bounds = sourceBounds;
                hasBounds = true;
            }
        }

        if (hasBounds)
            bounds.size = Vector3.Max(Vector3.Scale(bounds.size, _sizeMultiplier) + _padding * 2f, _minimumSize);
        return hasBounds;
    }

#if UNITY_EDITOR
    private void RefreshSceneView()
    {
        if (this == null || !isActiveAndEnabled || !_showPreview || Application.isPlaying)
            return;

        // 편집 모드의 Update는 매 프레임 실행되지 않으므로 Scene 뷰만 갱신합니다.
        // Simulate/Play를 호출하지 않아 Unity의 파티클 미리보기와 시간 스크럽을 유지합니다.
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now < _nextRepaintTime)
            return;
        _nextRepaintTime = now + 1.0 / 30.0;

        CollectSources();
        bool hasParticles = false;
        foreach (ParticleSystem particle in _sources)
        {
            if (particle != null && particle.gameObject.activeInHierarchy && particle.particleCount > 0)
            {
                hasParticles = true;
                break;
            }
        }

        // 마지막 파티클이 사라진 프레임에도 다시 그려 이전 박스를 지웁니다.
        if (hasParticles || _hadParticles)
            UnityEditor.SceneView.RepaintAll();
        _hadParticles = hasParticles;
    }

    private void OnDrawGizmos()
    {
        if (!isActiveAndEnabled || !_showPreview)
            return;

        Camera previewCamera = Camera.current;
        if (previewCamera == null)
            return;

        if (!TryGetHitboxBounds(previewCamera, out Bounds bounds, out Matrix4x4 boxToWorld))
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        try
        {
            Gizmos.matrix = boxToWorld;
            if (_showSourceBounds)
            {
                Gizmos.color = Color.cyan;
                foreach (Bounds sourceBounds in _sourceBounds)
                    Gizmos.DrawWireCube(sourceBounds.center, sourceBounds.size);
            }

            Gizmos.color = new Color(_previewColor.r, _previewColor.g, _previewColor.b, _previewColor.a * 0.06f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = _previewColor;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
        finally
        {
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
#endif

    private bool TryGetSlashBounds(ParticleSystem particle, Camera previewCamera,
        Matrix4x4 worldToBox, out Bounds bounds)
    {
        bounds = default;
        if (particle == null || !particle.gameObject.activeInHierarchy || particle.particleCount == 0
            || !particle.TryGetComponent(out ParticleSystemRenderer particleRenderer)
            || !particleRenderer.enabled || particleRenderer.renderMode != ParticleSystemRenderMode.Mesh)
        {
            return false;
        }

        if (_bakedMesh == null)
        {
            _bakedMesh = new Mesh
            {
                name = "Projectile Hitbox Preview (Temporary)",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
        }

        // 실제 Mesh를 읽어 Local/World/Custom 공간, 파티클 회전·크기, Renderer Pivot을 반영합니다.
        // 셰이더의 투명도와 정점 변형은 반영되지 않으므로 보이는 픽셀과 완전히 일치하지는 않습니다.
        _bakedMesh.Clear();
        particleRenderer.BakeMesh(_bakedMesh, previewCamera,
            ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
        _bakedMesh.GetVertices(_vertices);
        if (_vertices.Count == 0)
            return false;

        bounds = new Bounds(worldToBox.MultiplyPoint3x4(_vertices[0]), Vector3.zero);
        for (int i = 1; i < _vertices.Count; i++)
            bounds.Encapsulate(worldToBox.MultiplyPoint3x4(_vertices[i]));
        return true;
    }
}
