using System.Collections.Generic;
using UnityEngine;

public class EnemyHitboxPool : MonoBehaviour
{
    [SerializeField] private EnemyBoxHitbox _boxHitboxPrefab;
    [SerializeField] private EnemySphereHitbox _sphereHitboxPrefab;
    [SerializeField] private int _boxHitboxPoolCount = 5;
    [SerializeField] private int _sphereHitboxPoolCount = 5;
    [SerializeField] private Transform _hitboxContainer;

    private readonly List<EnemyBoxHitbox> _boxHitboxes = new();
    private readonly List<EnemySphereHitbox> _sphereHitboxes = new();
    private readonly HashSet<IDamageable> _targetsInCurrentDetection = new();
    private readonly List<IHitStopParticipant> _hitStopVictims = new();

    private IHitStopParticipant _ownerHitStopParticipant;
    private EnemyCore _enemyCore;
    private int _boxUsageFrame = -1;
    private int _sphereUsageFrame = -1;
    private int _usedBoxHitboxCount;
    private int _usedSphereHitboxCount;

    private void Awake()
    {
        _ownerHitStopParticipant = GetComponentInParent<IHitStopParticipant>();
        TryGetComponent(out _enemyCore);

        for (int i = 0; i < _boxHitboxPoolCount; i++)
            CreateBoxHitbox();

        for (int i = 0; i < _sphereHitboxPoolCount; i++)
            CreateSphereHitbox();
    }

    public void SpacingHitboxes(EnemyAttackSO enemyAttackSO)
    {
        if (enemyAttackSO == null || enemyAttackSO.HitboxInfo == null)
            return;

        _hitStopVictims.Clear();
        foreach (EnemyAttackHitboxInfo hitboxInfo in enemyAttackSO.HitboxInfo)
        {
            EnemyHitbox hitbox = GetHitbox(hitboxInfo.HitboxType);
            if (hitbox == null)
                continue;

            hitbox.Configure(hitboxInfo);
            GiveDamage(hitbox.DetectTargets(enemyAttackSO.DamagedLayer), hitboxInfo, enemyAttackSO.HitStopFrame);
        }

        HitstopCoordinator.Request(_ownerHitStopParticipant, _hitStopVictims, enemyAttackSO.HitStopFrame);
    }

    private EnemyHitbox GetHitbox(EnemyAttackHitboxType hitboxType)
    {
        switch (hitboxType)
        {
            case EnemyAttackHitboxType.Box:
                RefreshBoxUsageForCurrentFrame();
                if (_usedBoxHitboxCount >= _boxHitboxes.Count && !CreateBoxHitbox())
                    return null;

                return _boxHitboxes[_usedBoxHitboxCount++];

            case EnemyAttackHitboxType.Sphere:
                RefreshSphereUsageForCurrentFrame();
                if (_usedSphereHitboxCount >= _sphereHitboxes.Count && !CreateSphereHitbox())
                    return null;

                return _sphereHitboxes[_usedSphereHitboxCount++];

            default:
                Debug.LogWarning($"Unsupported enemy hitbox type: {hitboxType}", this);
                return null;
        }
    }

    private void GiveDamage(Collider[] hits, EnemyAttackHitboxInfo hitboxInfo, int hitStopFrame)
    {
        _targetsInCurrentDetection.Clear();

        foreach (Collider hit in hits)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !_targetsInCurrentDetection.Add(damageable))
                continue;

            DamageData damageData = new DamageData(gameObject, hitboxInfo.DamageAmount, hitStopFrame);
            if (!damageable.TryTakeDamage(damageData))
                continue;

            if (damageable is PlayerCore)
                _enemyCore?.CloseAttackHitSFX();

            if (damageable is IHitStopParticipant participant && !_hitStopVictims.Contains(participant))
                _hitStopVictims.Add(participant);
        }
    }

    private void RefreshBoxUsageForCurrentFrame()
    {
        if (_boxUsageFrame == Time.frameCount)
            return;

        _boxUsageFrame = Time.frameCount;
        _usedBoxHitboxCount = 0;
    }

    private void RefreshSphereUsageForCurrentFrame()
    {
        if (_sphereUsageFrame == Time.frameCount)
            return;

        _sphereUsageFrame = Time.frameCount;
        _usedSphereHitboxCount = 0;
    }

    private bool CreateBoxHitbox()
    {
        if (_boxHitboxPrefab == null || _hitboxContainer == null)
        {
            Debug.LogWarning("Enemy Box Hitbox prefab or container is not assigned.", this);
            return false;
        }

        _boxHitboxes.Add(Instantiate(_boxHitboxPrefab, _hitboxContainer));
        return true;
    }

    private bool CreateSphereHitbox()
    {
        if (_sphereHitboxPrefab == null || _hitboxContainer == null)
        {
            Debug.LogWarning("Enemy Sphere Hitbox prefab or container is not assigned.", this);
            return false;
        }

        _sphereHitboxes.Add(Instantiate(_sphereHitboxPrefab, _hitboxContainer));
        return true;
    }
}
