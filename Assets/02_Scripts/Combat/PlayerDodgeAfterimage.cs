using System;
using ProjectR.VFX;
using UnityEngine;

/// <summary>Optional per-player VFX owned by the Dodge Studio runtime, using its action snapshot.</summary>
public sealed class PlayerDodgeAfterimage : IDisposable
{
    readonly PlayerCore core;
    DodgeAfterimageSettings settings;
    GameObject prefab;
    ChromaticAfterimageTrail effect;
    bool triggered;
    public ChromaticAfterimageTrail Effect => effect;
    public bool UsesLegacy => settings == null || settings.UsesLegacy;

    public PlayerDodgeAfterimage(PlayerCore owner) { core = owner; }

    public void Begin(DodgeAfterimageSettings actionSettings)
    {
        effect?.StopEmission();
        settings = actionSettings;
        triggered = false;
        if (effect != null && (settings == null || !settings.WantsChromatic || prefab != settings.prefab)) DisposeEffect();
    }

    public void Trigger(DodgeAfterimageStart at)
    {
        if (triggered || settings == null || !settings.WantsChromatic || settings.start != at || !settings.HasValidPrefab || core.Animator == null) return;
        triggered = true;
        bool rebind = effect == null || effect.sourceRoot != core.Animator.transform ||
            effect.includeRigidMeshes != settings.includeRigidMeshes || effect.maxSnapshots != Mathf.Clamp(settings.maxSnapshots, 2, 24);
        if (effect == null)
        {
            prefab = settings.prefab;
            var instance = UnityEngine.Object.Instantiate(prefab, core.transform, false);
            instance.name = "Dodge Chromatic Afterimage";
            instance.hideFlags = HideFlags.DontSave;
            // Use the player's visible layer, rather than the standalone preview layer.
            foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = core.Animator.gameObject.layer;
            effect = instance.GetComponent<ChromaticAfterimageTrail>();
        }
        settings.ApplyTo(effect);
        effect.externalClock = true;
        if (rebind) effect.Bind(core.Animator.transform);
        ApplyParticleClock();
        effect.Play(Mathf.Max(.01f, settings.emissionDuration));
    }

    public void LateTick()
    {
        if (effect == null || settings == null) return;
        ApplyParticleClock();
        if (core.IsHitStopped || Time.timeScale <= 0) return;
        effect.Tick(settings.useRealTime ? Time.unscaledDeltaTime : CombatTimeController.DeltaTime);
    }

    void ApplyParticleClock()
    {
        if (effect.particles == null) return;
        effect.particles.pause = core.IsHitStopped || Time.timeScale <= 0;
        effect.particles.playRate = settings.useRealTime ? 1f / Mathf.Max(.0001f, Time.timeScale) : CombatTimeController.Scale;
    }

    public void EndMotion() { if (settings == null || settings.stopOnDodgeExit) effect?.StopEmission(); }
    void DisposeEffect()
    {
        if (effect != null)
        {
            effect.Clear();
            if (Application.isPlaying) UnityEngine.Object.Destroy(effect.gameObject);
            else UnityEngine.Object.DestroyImmediate(effect.gameObject);
        }
        effect = null; prefab = null;
    }
    public void Dispose() { DisposeEffect(); settings = null; triggered = false; }
}
