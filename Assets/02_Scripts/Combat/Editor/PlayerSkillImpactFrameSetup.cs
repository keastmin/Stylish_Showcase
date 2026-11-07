using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

public static class PlayerSkillImpactFrameSetup
{
    public const string TimelinePath = "Assets/04_Timelines/Player/SkillTimeline.playable";
    public const string PrefabPath = "Assets/08_Prefabs/Combat/Player.prefab";
    public const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    public const string EffectPath = "Assets/Download Assets/Vefects/Easy Impact Frames/VFX/Impact Frames/Particles/VFX_Impact_Frame_01.prefab";
    public const string PostProcessingProfilePath = "Assets/Settings/Skill Impact Frame Volume.asset";
    public const string MaterialPath = "Assets/09_Shaders/Impact Frame/Skill Impact Frame.mat";
    public const string ShaderPath = "Assets/09_Shaders/Impact Frame/SkillImpactFrame.shader";
    public const string SilhouetteMaterialPath = "Assets/09_Shaders/Impact Frame/Player Impact Silhouette.mat";
    public const string SilhouetteShaderPath = "Assets/09_Shaders/Impact Frame/PlayerImpactInput.shader";
    private const string AccentSourcePath = "Assets/Download Assets/Vefects/Easy Impact Frames/VFX/Extra/Particles/VFX_Extra_Eye_Candy_01.prefab";
    private const string RequestPath = "Temp/PlayerSkillImpactFrameSetup.request";
    private const string ResultPath = "Temp/PlayerSkillImpactFrameSetup.result";

    [InitializeOnLoadMethod]
    private static void RunRequestedSetup()
    {
        if (!File.Exists(RequestPath))
            return;
        EditorApplication.delayCall += () =>
        {
            File.Delete(RequestPath);
            try
            {
                SetUp();
                TimelineActivationVerification.Validate();
                File.WriteAllText(ResultPath, "PASS: setup and activation verification");
            }
            catch (Exception exception)
            {
                File.WriteAllText(ResultPath, "FAIL: " + exception);
                Debug.LogException(exception);
            }
        };
    }

    [MenuItem("Tools/Combat/Set Up Skill Impact Frame")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the skill in Edit mode.");

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        var effectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath);
        if (timeline == null || effectPrefab == null)
            throw new InvalidOperationException("SkillTimeline or Easy Impact Frames prefab is missing.");

        var track = timeline.GetOutputTracks().OfType<TimelineActivationTrack>()
            .SingleOrDefault(item => item.name == "Impact Frame");
        if (track == null)
            track = timeline.CreateTrack<TimelineActivationTrack>(null, "Impact Frame");
        if (!track.GetClips().Any())
        {
            TimelineClip clip = track.CreateClip<TimelineActivationClip>();
            clip.displayName = "Impact Frame";
            clip.start = 43d / 60d;
            clip.duration = 2d / 60d;
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
        }
        AssetDatabase.SaveAssetIfDirty(timeline);

        VolumeProfile impactProfile = GetPostProcessingProfile();
        Material impactMaterial = GetImpactMaterial(effectPrefab);

        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var director = prefab.GetComponentsInChildren<PlayableDirector>(true)
                .Single(item => item.playableAsset == timeline);
            var target = director.GetComponent<PlayerSkillImpactFrame>();
            if (target == null)
                target = director.gameObject.AddComponent<PlayerSkillImpactFrame>();
            Transform existing = prefab.transform.Find("Skill Impact Frame");
            var effect = existing != null ? existing.gameObject :
                (GameObject)PrefabUtility.InstantiatePrefab(effectPrefab, prefab.transform);
            effect.name = "Skill Impact Frame";
            effect.SetActive(false);
            effect.transform.localPosition = Vector3.up;
            effect.transform.localRotation = Quaternion.identity;
            // The same activation resource owns particles and post processing, so
            // scrubbing, hitstop and interruption all restore the scene together.
            var volume = effect.GetComponent<Volume>();
            if (volume == null)
                volume = effect.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 2000f;
            volume.weight = 1f;
            volume.sharedProfile = impactProfile;
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                Transform accents = effect.transform.Find("Impact Accents");
                if (accents != null && particle.transform.IsChildOf(accents)) continue;
                var main = particle.main;
                main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpeed = 1f;
                particle.useAutoRandomSeed = false;
                particle.randomSeed = 12345;
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var particleRenderer = particle.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null && particleRenderer.sharedMaterial != null)
                    particleRenderer.sharedMaterial = impactMaterial;
            }
            ConfigureAccents(effect.GetComponent<ParticleSystem>());
            target.Configure(prefab.transform, effect.GetComponent<ParticleSystem>(), track, GetSilhouetteMaterial(prefab));
            director.SetGenericBinding(track, target);
            // The old full-screen pass must no longer stack over the vendor effect.
            foreach (var old in director.GetComponents<RendererFeatureActivationTarget>())
                if (old.Feature != null && old.Feature.name == "Impact Frame")
                    UnityEngine.Object.DestroyImmediate(old);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        Debug.Log("Skill Impact Frame: particles and Bloom/Motion Blur suppression share the Impact Frame clip; existing preparation timing retained.");
    }

    // Updates only E impact presentation on an already configured player prefab.
    public static void UpgradeQuality()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Upgrade the skill in Edit mode.");
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var target = prefab.GetComponentInChildren<PlayerSkillImpactFrame>(true);
            if (target == null || target.Effect == null) throw new InvalidOperationException("Set up the E impact first.");
            GetImpactMaterial(AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath));
            GetSilhouetteMaterial(prefab);
            ConfigureAccents(target.Effect);
            var serialized = new SerializedObject(target);
            serialized.FindProperty("_coreScreenScale").floatValue = 9f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static void ConfigureAccents(ParticleSystem effect)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AccentSourcePath);
        if (source == null) throw new InvalidOperationException("Demo accent particles are missing.");
        Transform existing = effect.transform.Find("Impact Accents");
        var root = existing != null ? existing.gameObject : new GameObject("Impact Accents");
        if (existing == null) root.transform.SetParent(effect.transform, false);
        var systems = source.GetComponentsInChildren<ParticleSystem>(true);
        foreach (string name in new[] { "Zap", "Zap BG", "Flare" })
        {
            Transform child = root.transform.Find(name);
            var particle = child != null ? child.GetComponent<ParticleSystem>()
                : UnityEngine.Object.Instantiate(systems.Single(item => item.name == name), root.transform);
            particle.name = name;
            particle.gameObject.SetActive(true);
            var main = particle.main;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            if (name == "Flare") main.startColor = Color.white;
            particle.useAutoRandomSeed = false;
            particle.randomSeed = 12345;
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = particle.GetComponent<ParticleSystemRenderer>();
            string materialPath = "Assets/09_Shaders/Impact Frame/Skill Impact " + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial) { name = "Skill Impact " + name, renderQueue = 3100 };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            renderer.sharedMaterial = material;
        }
    }

    private static VolumeProfile GetPostProcessingProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessingProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Skill Impact Frame Volume";
            AssetDatabase.CreateAsset(profile, PostProcessingProfilePath);
        }

        // Only these intensities override the scene. Vignette and other combat
        // effects continue to use their own volumes and runtime profiles.
        if (!profile.TryGet<Bloom>(out _))
        {
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0f);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
        }
        if (!profile.TryGet<MotionBlur>(out _))
        {
            var motionBlur = profile.Add<MotionBlur>();
            motionBlur.intensity.Override(0f);
            AssetDatabase.AddObjectToAsset(motionBlur, profile);
            EditorUtility.SetDirty(motionBlur);
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssetIfDirty(profile);
        return profile;
    }

    private static Material GetImpactMaterial(GameObject effectPrefab)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Material source = effectPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Select(renderer => renderer.sharedMaterial).First(item => item != null);
            material = new Material(source) { name = "Skill Impact Frame" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) throw new InvalidOperationException("E impact shader is missing.");
        material.shader = shader;
        material.SetFloat("_RadialDistortionAmount", .01f);
        material.enabledKeywords = Array.Empty<UnityEngine.Rendering.LocalKeyword>();
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static Material GetSilhouetteMaterial(GameObject player)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(SilhouetteMaterialPath);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SilhouetteShaderPath);
        if (shader == null) throw new InvalidOperationException("Player impact input shader is missing.");
        if (material == null)
        {
            material = new Material(shader) { name = "Player Impact Silhouette" };
            AssetDatabase.CreateAsset(material, SilhouetteMaterialPath);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }
}
