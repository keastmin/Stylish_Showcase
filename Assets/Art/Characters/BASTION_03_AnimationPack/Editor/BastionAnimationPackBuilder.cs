using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectR.Art.Editor
{
    /// <summary>Explicit, isolated import and validation for the BASTION animation pack.</summary>
    public static class BastionAnimationPackBuilder
    {
        public const string Folder = "Assets/Art/Characters/BASTION_03_AnimationPack";
        private const string ModelPath = Folder + "/Models/BASTION_03.fbx";
        private const string ControllerPath = Folder + "/Animator/BASTION_03.controller";
        private const string PrefabPath = Folder + "/Prefabs/BASTION_03.prefab";
        private static readonly string[] ClipNames = { "Idle", "Walk", "BladeSlash", "GatlingFire", "MissileLaunch" };

        // These transport fields are populated by Unity's JSON serializer.
#pragma warning disable CS0649
        [Serializable] private sealed class Manifest
        {
            public string colorSpace = "linear";
            public MaterialEntry[] materials;
            public ClipEntry[] clips;
        }

        [Serializable] private sealed class MaterialEntry
        {
            public string name;
            public float[] baseColor;
            public float metallic;
            public float roughness = .5f;
            public float[] emission;
            public float emissionStrength;
        }

        [Serializable] private sealed class ClipEntry
        {
            public string name;
            public string filename;
            public bool loop;
        }
#pragma warning restore CS0649

        [Serializable] private sealed class ClipReport
        {
            public string name;
            public float duration;
            public float frameRate;
            public bool loop;
            public int curveCount;
            public string[] missingPaths;
            public float[] sampleTimes;
            public float maximumSampledPositionDelta;
            public float maximumSampledRotationDegrees;
        }

        [Serializable] private sealed class ValidationReport
        {
            public string unityVersion;
            public string prefab;
            public string controller;
            public int renderers;
            public int skinnedMeshes;
            public int materialCount;
            public int bones;
            public bool avatarValid;
            public bool activeScenePreserved;
            public Vector3 modelSize;
            public ClipReport[] clips;
            public string[] errors;
            public bool passed;
        }

        [MenuItem("Tools/Project R/BASTION 03/Build Animation Pack")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build BASTION outside Play Mode.");
            string manifestPath = Folder + "/manifest.json";
            if (!File.Exists(ModelPath) || !File.Exists(manifestPath))
                throw new FileNotFoundException("BASTION model FBX and manifest.json must be exported first.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.materials == null || manifest.materials.Length == 0)
                throw new InvalidDataException("BASTION manifest has no materials.");

            foreach (string directory in new[] { "Materials", "Animations/Clips", "Animator", "Prefabs", "Validation" })
                Directory.CreateDirectory(Folder + "/" + directory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, Material> materials = BuildMaterials(manifest);
            ConfigureModel(ModelPath, false, null);
            ModelImporter modelImporter = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            foreach (Material source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
            {
                Material target;
                if (TryFindMaterial(materials, source.name, out target))
                    modelImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name), target);
            }
            modelImporter.SaveAndReimport();
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid)
                throw new InvalidDataException("The BASTION base model did not import a valid Generic Avatar.");

            var clips = new Dictionary<string, AnimationClip>();
            foreach (string clipName in ClipNames)
            {
                ClipEntry entry = manifest.clips == null ? null : manifest.clips.FirstOrDefault(c => c.name == clipName);
                string animationPath = ResolveAnimationPath(entry, clipName);
                bool loop = entry != null ? entry.loop : clipName == "Idle" || clipName == "Walk";
                ConfigureModel(animationPath, true, avatar);
                ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(animationPath);
                ModelImporterClipAnimation[] definitions = importer.defaultClipAnimations;
                if (definitions.Length == 0)
                    throw new InvalidDataException(animationPath + " has no animation take.");
                ModelImporterClipAnimation definition = definitions[0];
                definition.name = clipName;
                definition.loopTime = loop;
                definition.loopPose = false;
                definition.lockRootRotation = true;
                definition.lockRootHeightY = true;
                definition.lockRootPositionXZ = true;
                definition.keepOriginalOrientation = true;
                definition.keepOriginalPositionY = true;
                definition.keepOriginalPositionXZ = true;
                importer.clipAnimations = new[] { definition };
                importer.SaveAndReimport();
                AnimationClip imported = AssetDatabase.LoadAllAssetsAtPath(animationPath)
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                if (imported == null || imported.length <= 0)
                    throw new InvalidDataException(animationPath + " has an empty animation.");
                AnimationClip copy = UnityEngine.Object.Instantiate(imported);
                copy.name = clipName;
                copy.legacy = false;
                string path = Folder + "/Animations/Clips/" + clipName + ".anim";
                AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(copy, existing);
                    UnityEngine.Object.DestroyImmediate(copy);
                    EditorUtility.SetDirty(existing);
                    clips.Add(clipName, existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(copy, path);
                    clips.Add(clipName, copy);
                }
            }
            AnimatorController controller = BuildController(clips);
            BuildPrefabAndValidate(materials, avatar, controller, clips);
            SaveGeneratedAssets(materials.Values, clips.Values, controller);
            AssetDatabase.ImportAsset(Folder + "/Validation/unity_validation.json", ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[BASTION] Built and validated " + PrefabPath + " with five Generic animation clips.");
        }

        private static void SaveGeneratedAssets(IEnumerable<Material> materials, IEnumerable<AnimationClip> clips,
            AnimatorController controller)
        {
            // Save only assets owned by this pack, never unrelated user assets that are currently dirty.
            foreach (Material material in materials) AssetDatabase.SaveAssetIfDirty(material);
            foreach (AnimationClip clip in clips) AssetDatabase.SaveAssetIfDirty(clip);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.SaveAssetIfDirty(controller);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) AssetDatabase.SaveAssetIfDirty(prefab);
        }

        private static string ResolveAnimationPath(ClipEntry entry, string clipName)
        {
            string path = entry == null || string.IsNullOrEmpty(entry.filename)
                ? "Animations/FBX/BASTION_03_" + clipName + ".fbx" : entry.filename.Replace('\\', '/');
            if (!path.StartsWith(Folder + "/", StringComparison.Ordinal))
            {
                if (!path.Contains("/")) path = "Animations/FBX/" + path;
                path = Folder + "/" + path;
            }
            if (!File.Exists(path)) throw new FileNotFoundException("BASTION animation FBX is missing.", path);
            return path;
        }

        private static void ConfigureModel(string path, bool animation, Avatar avatar)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidDataException("Not an FBX model: " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = animation ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
            importer.motionNodeName = "root";
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importAnimation = animation;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = animation ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, Material> BuildMaterials(Manifest manifest)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is missing.");
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            bool linear = !string.Equals(manifest.colorSpace, "srgb", StringComparison.OrdinalIgnoreCase);
            foreach (MaterialEntry entry in manifest.materials)
            {
                if (string.IsNullOrWhiteSpace(entry.name)) throw new InvalidDataException("Material name is empty.");
                string safeName = string.Concat(entry.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c));
                string path = Folder + "/Materials/" + safeName + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = entry.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                Color baseColor = ReadColor(entry.baseColor, Color.gray);
                if (linear) baseColor = baseColor.gamma;
                material.SetColor("_BaseColor", baseColor);
                material.SetFloat("_Metallic", Mathf.Clamp01(entry.metallic));
                material.SetFloat("_Smoothness", 1 - Mathf.Clamp01(entry.roughness));
                Color emission = ReadColor(entry.emission, Color.black);
                if (linear) emission = emission.gamma;
                emission *= Mathf.Max(0, entry.emissionStrength);
                material.SetColor("_EmissionColor", emission);
                if (emission.maxColorComponent > 0)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                else material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                result.Add(entry.name, material);
            }
            return result;
        }

        private static Color ReadColor(float[] value, Color fallback)
        {
            return value == null || value.Length < 3 ? fallback : new Color(value[0], value[1], value[2], value.Length > 3 ? value[3] : 1);
        }

        private static bool TryFindMaterial(Dictionary<string, Material> materials, string source, out Material material)
        {
            if (materials.TryGetValue(source, out material)) return true;
            string normalized = NormalizeName(source);
            foreach (KeyValuePair<string, Material> pair in materials)
                if (NormalizeName(pair.Key) == normalized) { material = pair.Value; return true; }
            material = null;
            return false;
        }

        private static string NormalizeName(string name)
        {
            return string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            // This path is exclusively generated by this builder.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (string name in ClipNames.Skip(2)) controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>();
            for (int i = 0; i < ClipNames.Length; i++)
            {
                string name = ClipNames[i];
                AnimatorState state = machine.AddState(name, new Vector3(280 + (i % 2) * 260, 60 + (i / 2) * 100));
                state.motion = clips[name];
                state.writeDefaultValues = false;
                states.Add(name, state);
            }
            machine.defaultState = states["Idle"];
            AnimatorStateTransition walk = states["Idle"].AddTransition(states["Walk"]);
            ConfigureTransition(walk, false, .15f);
            walk.AddCondition(AnimatorConditionMode.Greater, .1f, "Speed");
            AnimatorStateTransition idle = states["Walk"].AddTransition(states["Idle"]);
            ConfigureTransition(idle, false, .15f);
            idle.AddCondition(AnimatorConditionMode.Less, .1f, "Speed");
            foreach (string name in ClipNames.Skip(2))
            {
                AnimatorStateTransition attack = machine.AddAnyStateTransition(states[name]);
                ConfigureTransition(attack, false, .08f);
                attack.canTransitionToSelf = false;
                attack.AddCondition(AnimatorConditionMode.If, 0, name);
                AnimatorStateTransition recover = states[name].AddTransition(states["Idle"]);
                ConfigureTransition(recover, true, .12f);
                recover.exitTime = 1;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, bool exitTime, float duration)
        {
            transition.hasExitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        private static void BuildPrefabAndValidate(Dictionary<string, Material> materials, Avatar avatar,
            AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var preview = EditorSceneManager.NewPreviewScene();
            var report = new ValidationReport { unityVersion = Application.unityVersion, prefab = PrefabPath,
                controller = ControllerPath, avatarValid = avatar.isValid, materialCount = materials.Count };
            var errors = new List<string>();
            GameObject instance = null;
            try
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                instance.name = "BASTION_03";
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        Material mapped;
                        if (slots[i] != null && TryFindMaterial(materials, slots[i].name, out mapped)) slots[i] = mapped;
                        else errors.Add("Unmapped material on " + renderer.name + ": " + (slots[i] == null ? "null" : slots[i].name));
                    }
                    renderer.sharedMaterials = slots;
                }
                SkinnedMeshRenderer[] skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.renderers = instance.GetComponentsInChildren<Renderer>(true).Length;
                report.skinnedMeshes = skins.Length;
                report.bones = skins.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count();
                if (report.renderers == 0) errors.Add("No model renderers.");
                if (skins.Length == 0) errors.Add("No skinned model meshes.");
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    report.modelSize = bounds.size;
                    if (bounds.size.y < 2.5f || bounds.size.y > 3.8f)
                        errors.Add("Unexpected model height in metres: " + bounds.size.y);
                }
                foreach (SkinnedMeshRenderer skin in skins)
                {
                    if (skin.sharedMesh == null) errors.Add("Missing mesh: " + skin.name);
                    if (skin.bones.Any(b => b == null)) errors.Add("Missing bones: " + skin.name);
                    skin.updateWhenOffscreen = true;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                animator.enabled = false;
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                Vector3[] locations = transforms.Select(t => t.localPosition).ToArray();
                Quaternion[] rotations = transforms.Select(t => t.localRotation).ToArray();
                Vector3[] scales = transforms.Select(t => t.localScale).ToArray();
                var clipReports = new List<ClipReport>();
                foreach (KeyValuePair<string, AnimationClip> pair in clips)
                {
                    EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(pair.Value);
                    string[] missing = bindings.Where(b => b.type == typeof(Transform) && !string.IsNullOrEmpty(b.path)
                        && instance.transform.Find(b.path) == null).Select(b => b.path).Distinct().ToArray();
                    if (missing.Length > 0) errors.Add(pair.Key + " has " + missing.Length + " missing animation paths.");
                    if (bindings.Length == 0) errors.Add(pair.Key + " has no animation curves.");
                    float[] times = { 0, pair.Value.length * .25f, pair.Value.length * .5f, pair.Value.length * .75f, pair.Value.length };
                    Vector3[] firstLocations = null;
                    Quaternion[] firstRotations = null;
                    float maximumPositionDelta = 0;
                    float maximumRotationDegrees = 0;
                    foreach (float time in times)
                    {
                        for (int i = 0; i < transforms.Length; i++)
                        {
                            transforms[i].localPosition = locations[i];
                            transforms[i].localRotation = rotations[i];
                            transforms[i].localScale = scales[i];
                        }
                        pair.Value.SampleAnimation(instance, time);
                        if (firstLocations == null)
                        {
                            firstLocations = transforms.Select(t => t.localPosition).ToArray();
                            firstRotations = transforms.Select(t => t.localRotation).ToArray();
                        }
                        for (int i = 0; i < transforms.Length; i++)
                        {
                            maximumPositionDelta = Mathf.Max(maximumPositionDelta, Vector3.Distance(firstLocations[i], transforms[i].localPosition));
                            maximumRotationDegrees = Mathf.Max(maximumRotationDegrees, Quaternion.Angle(firstRotations[i], transforms[i].localRotation));
                        }
                        foreach (Transform transform in transforms)
                        {
                            Vector3 position = transform.localPosition;
                            Quaternion rotation = transform.localRotation;
                            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)
                                || !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w))
                                errors.Add(pair.Key + " has invalid pose at " + time + " on " + transform.name);
                        }
                    }
                    if (maximumPositionDelta < .00001f && maximumRotationDegrees < .01f)
                        errors.Add(pair.Key + " has no measurable motion on the model.");
                    clipReports.Add(new ClipReport { name = pair.Key, duration = pair.Value.length,
                        frameRate = pair.Value.frameRate, loop = pair.Value.isLooping, curveCount = bindings.Length,
                        missingPaths = missing, sampleTimes = times, maximumSampledPositionDelta = maximumPositionDelta,
                        maximumSampledRotationDegrees = maximumRotationDegrees });
                }
                report.clips = clipReports.ToArray();
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
                report.activeScenePreserved = UnityEngine.SceneManagement.SceneManager.GetActiveScene() == activeScene;
                if (!report.activeScenePreserved) errors.Add("Active scene changed during validation.");
                report.errors = errors.Distinct().ToArray();
                report.passed = report.errors.Length == 0 && report.clips != null && report.clips.Length == ClipNames.Length;
                File.WriteAllText(Folder + "/Validation/unity_validation.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidDataException("BASTION validation failed: " + string.Join("; ", report.errors));
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
