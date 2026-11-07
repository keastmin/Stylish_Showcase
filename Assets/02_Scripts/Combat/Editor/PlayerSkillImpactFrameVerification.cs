using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Disposable pose/effect captures in the current environment; never saves the scene.</summary>
public static class PlayerSkillImpactFrameVerification
{
    public const string Output = "Temp/ImpactFrameQuality20261107";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture in Edit mode.");
        Directory.CreateDirectory(Output);
        var source = Object.FindAnyObjectByType<PlayerSkillImpactFrame>(FindObjectsInactive.Include);
        if (source == null || Camera.main == null) throw new InvalidOperationException("Open the prototype scene.");
        var player = source.GetComponentInParent<PlayerCore>();
        var director = source.GetComponent<PlayableDirector>();
        var definition = director.GetComponent<PlayerSkillSequenceSettings>().Definition;
        var preview = new ESkillDataPreview();
        GameObject effect = null, cameraObject = null;
        var originals = new List<(Renderer renderer, bool hidden)>();
        object owner = new object();
        PlayerSkillImpactFrame impact = null;
        Material baseline = null;
        try
        {
            preview.Build(player, director, director.playableAsset as TimelineAsset);
            var root = (GameObject)typeof(ESkillDataPreview).GetField("_root", Hidden).GetValue(preview);
            var model = (GameObject)typeof(ESkillDataPreview).GetField("_model", Hidden).GetValue(preview);
            foreach (Renderer renderer in player.Animator.GetComponentsInChildren<Renderer>(true))
            {
                originals.Add((renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }
            effect = Object.Instantiate(source.Effect.gameObject);
            effect.name = "Disposable E impact capture";
            effect.hideFlags = HideFlags.HideAndDontSave;
            effect.transform.SetParent(null);
            effect.SetActive(false);
            impact = root.AddComponent<PlayerSkillImpactFrame>();
            impact.Configure(root.transform, effect.GetComponent<ParticleSystem>(), null,
                AssetDatabase.LoadAssetAtPath<Material>(PlayerSkillImpactFrameSetup.SilhouetteMaterialPath));
            impact.BeginExternalSession(owner);
            cameraObject = new GameObject("Disposable E capture camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            EditorUtility.CopySerialized(Camera.main.GetUniversalAdditionalCameraData(), camera.GetUniversalAdditionalCameraData());
            var material = AssetDatabase.LoadAssetAtPath<Material>(PlayerSkillImpactFrameSetup.MaterialPath);
            var renderers = effect.GetComponentsInChildren<ParticleSystemRenderer>(true).Where(r => r.sharedMaterial == material).ToArray();
            baseline = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            baseline.shader = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSkillImpactFrameSetup.EffectPath)
                .GetComponentsInChildren<ParticleSystemRenderer>(true).First(r => r.sharedMaterial != null).sharedMaterial.shader;
            baseline.SetFloat("_RadialDistortionAmount", .00025f);
            foreach (float ms in new[] { 17f, 50f, 100f, 150f, 200f, 267f })
            {
                float t = .53333336f + ms * .001f;
                preview.Sample(t, definition);
                root.transform.position += player.transform.position - preview.Origin;
                impact.SampleExternal(owner, true, ms / 300f);
                impact.PrepareForCamera(camera);
                if (ms == 100f)
                {
                    var block = new MaterialPropertyBlock();
                    renderers[0].GetPropertyBlock(block);
                    File.WriteAllText(Output + "/projection.txt", "scale=" + effect.transform.lossyScale
                        + " coreUV=" + block.GetFloat("_CoreUVScale") + " protection=" + block.GetVector("_PlayerProtection")
                        + " cam=" + camera.transform.position + " fov=" + camera.fieldOfView + " center=" + impact.Center);
                }
                Save(camera, $"{Output}/after_{ms:000}ms.png");
                if (ms == 100f)
                    File.AppendAllText(Output + "/projection.txt", "\n" + string.Join("\n", effect.GetComponentsInChildren<ParticleSystem>(true)
                        .Select(ps => ps.name + " active=" + ps.gameObject.activeInHierarchy + " count=" + ps.particleCount
                            + " renderer=" + ps.GetComponent<ParticleSystemRenderer>().enabled
                            + " hidden=" + ps.GetComponent<ParticleSystemRenderer>().forceRenderingOff)));
                if (ms == 100f)
                {
                    // Same pose, camera and absolute particle time for the former material.
                    foreach (var renderer in renderers) renderer.sharedMaterial = baseline;
                    SaveLegacyPlayer(camera, impact, model, effect);
                    foreach (var renderer in renderers) renderer.sharedMaterial = material;
                }
            }
            // Scrub backwards after the tail. Both particle randomness and shader time must repeat.
            preview.Sample(.63333336f, definition);
            root.transform.position += player.transform.position - preview.Origin;
            impact.SampleExternal(owner, true, 1f / 3f);
            Save(camera, $"{Output}/replay_100ms.png");
            if (!File.ReadAllBytes(Output + "/after_100ms.png").SequenceEqual(File.ReadAllBytes(Output + "/replay_100ms.png")))
                throw new InvalidOperationException("The impact changed when scrubbing back to the same time.");
            Save(camera, Output + "/after_4x3.png", 960, 720);
            Save(camera, Output + "/after_ultrawide.png", 1440, 600);
            impact.SampleExternal(owner, false, 1f);
            if (effect.GetComponentsInChildren<ParticleSystem>(true).Any(ps => ps.particleCount != 0))
                throw new InvalidOperationException("Impact particles survived deactivation.");
            Save(camera, $"{Output}/restored.png");
            File.WriteAllText(Output + "/verification.txt", "PASS: six absolute-time samples; pixel-identical backwards scrub; 4:3/16:9/ultrawide coverage; deactivation clears all particles.\n");
            return "Captured six absolute-time frames and same-pose comparison in " + Output;
        }
        finally
        {
            if (impact != null) impact.EndExternalSession(owner);
            foreach (var entry in originals) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.hidden;
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (effect != null) Object.DestroyImmediate(effect);
            if (baseline != null) Object.DestroyImmediate(baseline);
            preview.Dispose();
        }
    }

    private static void SaveLegacyPlayer(Camera camera, PlayerSkillImpactFrame impact, GameObject model, GameObject effect)
    {
        var replacements = new List<(Renderer renderer, Material[] original)>();
        var temporary = new List<Material>();
        Transform accents = effect.transform.Find("Impact Accents");
        typeof(PlayerSkillImpactFrame).GetField("_emphasizePlayerSilhouette", Hidden).SetValue(impact, false);
        try
        {
            if (accents != null) accents.gameObject.SetActive(false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer) continue;
                var source = renderer.sharedMaterials;
                var bright = source.Select(item =>
                {
                    if (item == null) return null;
                    var copy = new Material(item) { hideFlags = HideFlags.HideAndDontSave };
                    if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", Color.white);
                    if (copy.HasProperty("_EmissionColor")) { copy.SetColor("_EmissionColor", Color.white * 8); copy.EnableKeyword("_EMISSION"); }
                    temporary.Add(copy);
                    return copy;
                }).ToArray();
                replacements.Add((renderer, source));
                renderer.sharedMaterials = bright;
            }
            Save(camera, Output + "/before_100ms.png");
        }
        finally
        {
            foreach (var entry in replacements) entry.renderer.sharedMaterials = entry.original;
            foreach (var material in temporary) Object.DestroyImmediate(material);
            if (accents != null) accents.gameObject.SetActive(true);
            typeof(PlayerSkillImpactFrame).GetField("_emphasizePlayerSilhouette", Hidden).SetValue(impact, true);
        }
    }

    public static string CaptureRuntime()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        bool paused = EditorApplication.isPaused;
        EditorApplication.isPaused = true;
        var player = Object.FindAnyObjectByType<PlayerCore>();
        bool input = player.InputCollector.enabled;
        var inactive = new List<GameObject>();
        try
        {
            player.InputCollector.enabled = false;
            foreach (var enemy in Object.FindObjectsByType<EnemyCore>())
            {
                inactive.Add(enemy.gameObject);
                enemy.gameObject.SetActive(false);
            }
            player.AddSkillGauge(100);
            player.StateMachine.Transition(player.StateMachine.SkillState);
            var runtime = player.StateMachine.SkillState.Runtime;
            runtime.Tick(.63333336f);
            var director = player.DirectorContainer.Directors[DirectorID.Skill];
            var impact = director.GetComponent<PlayerSkillImpactFrame>();
            if (!impact.Effect.gameObject.activeInHierarchy) throw new InvalidOperationException("Real E did not activate impact.");
            var camera = Camera.main;
            camera.GetComponent<CinemachineBrain>()?.ManualUpdate();
            Save(camera, Output + "/runtime_100ms.png");
            float frozen = runtime.Time;
            player.BeginHitStop();
            runtime.Tick(.2f);
            if (runtime.Time != frozen) throw new InvalidOperationException("Impact clock advanced in hitstop.");
            Save(camera, Output + "/runtime_hitstop.png");
            player.EndHitStop();
            player.StateMachine.Transition(player.StateMachine.IdleState);
            if (impact.Effect.gameObject.activeInHierarchy || impact.Effect.GetComponentsInChildren<ParticleSystem>(true).Any(ps => ps.particleCount != 0))
                throw new InvalidOperationException("Interrupted E did not clear impact and accents.");
            Save(camera, Output + "/runtime_restored.png");
            const string result = "PASS: real E activates impact; hitstop freezes its clock; interruption restores materials and clears accents.";
            File.AppendAllText(Output + "/verification.txt", result + "\n");
            return result;
        }
        finally
        {
            player.EndHitStop();
            if (player.StateMachine.IsSkillActive) player.StateMachine.Transition(player.StateMachine.IdleState);
            player.InputCollector.enabled = input;
            foreach (var enemy in inactive) if (enemy != null) enemy.SetActive(true);
            EditorApplication.isPaused = paused;
        }
    }

    private static void Save(Camera camera, string path, int width = 960, int height = 540)
    {
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
        }
    }
}
