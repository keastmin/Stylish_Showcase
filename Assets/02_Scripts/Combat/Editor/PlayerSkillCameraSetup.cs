using System;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public static class PlayerSkillCameraSetup
{
    public const string RigPath = "Assets/08_Prefabs/Combat/PlayerSkillCameraRig.prefab";
    public const string PositionSignalPath = "Assets/05_Timeline Signals/Player/Skill Camera Position.signal";
    public const string RecenterSignalPath = "Assets/05_Timeline Signals/Player/TPS Camera Recenter.signal";
    public static readonly string[] ScenePaths = {
        "Assets/01_Scenes/DemoScene 1.unity", "Assets/01_Scenes/CombatPrototypeScene.unity" };

    [InitializeOnLoadMethod]
    private static void RunRequestedSetup()
    {
        const string request = "Temp/SkillCameraSetup.request";
        if (!File.Exists(request))
            return;
        EditorApplication.delayCall += () =>
        {
            File.Delete(request);
            try
            {
                SetUp();
                SkillCameraVerification.Validate();
                File.WriteAllText("Temp/SkillCameraSetup.result", "PASS: environment setup and camera verification");
            }
            catch (Exception exception)
            {
                File.WriteAllText("Temp/SkillCameraSetup.result", "FAIL: " + exception);
                Debug.LogException(exception);
            }
        };
    }

    [MenuItem("Tools/Combat/Set Up Skill Camera Environment")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up cameras in Edit mode.");
        PlayerSkillCameraDirector.RestoreAllPreviews();
        TimelineActivationRegistry.RestoreAll();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(PlayerSkillImpactFrameSetup.TimelinePath);
        if (timeline == null)
            throw new InvalidOperationException("SkillTimeline is missing.");
        EnsureRigPrefab();
        var position = Signal(PositionSignalPath);
        var recenter = Signal(RecenterSignalPath);
        var legacy = timeline.GetOutputTracks().OfType<ControlTrack>()
            .FirstOrDefault(track => track.name == "Skill Camera" || track.name == "Camera 1");
        var legacyClip = legacy?.GetClips().FirstOrDefault();
        double start = legacyClip?.start ?? 7d / 60d;
        double end = legacyClip?.end ?? 104d / 60d;
        var cameraTracks = timeline.GetOutputTracks().OfType<TimelineActivationTrack>()
            .Where(track => track.name == "Skill Camera" || track.name.StartsWith("Skill Camera (", StringComparison.Ordinal)).ToArray();
        var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSkillImpactFrameSetup.PrefabPath);
        var configuredTrack = playerAsset?.GetComponentInChildren<PlayerSkillCameraDirector>(true)?.ActivationTrack;
        var activation = cameraTracks.FirstOrDefault(track => track == configuredTrack) ?? cameraTracks.FirstOrDefault();
        if (activation == null)
            activation = timeline.CreateTrack<TimelineActivationTrack>(null, "Skill Camera");
        activation.name = "Skill Camera";
        var obsoleteTracks = cameraTracks.Where(track => track != activation).Cast<TrackAsset>().ToList();
        if (legacy != null)
            obsoleteTracks.Add(legacy);
        var obsoleteReferences = legacy?.GetClips().Select(clip => (ControlPlayableAsset)clip.asset)
            .Select(clip => clip.sourceGameObject.exposedName).ToArray() ?? Array.Empty<PropertyName>();
        if (!activation.GetClips().Any())
        {
            var clip = activation.CreateClip<TimelineActivationClip>();
            clip.displayName = "Skill Camera Active";
            clip.start = start;
            clip.duration = end - start;
        }
        var cameraClip = activation.GetClips().First();
        var signals = timeline.GetOutputTracks().OfType<SkillCameraSignalTrack>().FirstOrDefault();
        if (signals == null)
            signals = timeline.CreateTrack<SkillCameraSignalTrack>(null, "Skill Camera Signals");
        AddMarker(signals, position, Math.Max(0d, cameraClip.start - 2d / 60d));
        AddMarker(signals, recenter, Math.Max(cameraClip.start, cameraClip.end - 0.2d));
        var noise = timeline.GetOutputTracks().OfType<CameraNoiseTrack>().FirstOrDefault();
        if (noise == null)
            noise = timeline.CreateTrack<CameraNoiseTrack>(null, "Skill Camera Noise");
        if (!noise.GetClips().Any())
        {
            var clip = noise.CreateClip<CameraNoiseClip>();
            clip.start = cameraClip.start + 0.2d;
            clip.duration = Math.Max(0.1d, cameraClip.end - 0.2d - clip.start);
            clip.displayName = "Weak Noise + Final Impact";
            var settings = (CameraNoiseClip)clip.asset;
            settings.Profile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Assets/13_Camera Noise/Skill Camera Noise Profile.asset");
            if (settings.Profile != null)
            {
                settings.Waveform = CameraNoiseClip.NoiseWaveform.Profile;
                settings.Amplitude = 0.01f;
                settings.Frequency = 1f;
            }
            EditorUtility.SetDirty(settings);
        }

        var oldSignals = new[] {
            SignalAt("Skill Cine Cam Parent Null Signal"), SignalAt("Skill Cine Cam Parent Model Signal") };
        foreach (var track in timeline.GetOutputTracks().ToArray())
            foreach (var marker in track.GetMarkers().OfType<SignalEmitter>().ToArray())
                if (oldSignals.Contains(marker.asset) && marker.asset != null)
                    track.DeleteMarker(marker);
        foreach (var track in timeline.GetOutputTracks())
            EditorUtility.SetDirty(track);
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssetIfDirty(timeline);

        var prefab = PrefabUtility.LoadPrefabContents(PlayerSkillImpactFrameSetup.PrefabPath);
        try
        {
            var director = SkillDirector(prefab);
            ClearObsoleteBindings(director, obsoleteTracks, obsoleteReferences);
            var controller = Component<PlayerSkillCameraDirector>(director.gameObject);
            controller.Configure(null, null, null, prefab.transform, Facing(prefab), null, position, recenter, activation);
            var receiver = Component<SignalReceiver>(director.gameObject);
            ConnectSignals(receiver, controller, position, recenter);
            Component<GameObjectActivationTarget>(director.gameObject).Configure(null);
            Component<CinemachineCameraNoiseTarget>(director.gameObject).Configure(null);
            director.SetGenericBinding(activation, director.GetComponent<GameObjectActivationTarget>());
            director.SetGenericBinding(noise, director.GetComponent<CinemachineCameraNoiseTarget>());
            director.SetGenericBinding(signals, receiver);
            // The scene rig replaces the old camera nested below the moving model.
            foreach (var camera in prefab.GetComponentsInChildren<CinemachineCamera>(true))
                if (camera.name.StartsWith("Skill Camera", StringComparison.Ordinal))
                    Object.DestroyImmediate(camera.gameObject);
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerSkillImpactFrameSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        Scene originalActive = SceneManager.GetActiveScene();
        foreach (string path in ScenePaths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SetUpScene(scene, position, recenter, activation, signals, noise, obsoleteTracks, obsoleteReferences);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save camera setup: " + path);
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        if (originalActive.IsValid() && originalActive.isLoaded)
            SceneManager.SetActiveScene(originalActive);
        foreach (var track in obsoleteTracks)
            timeline.DeleteTrack(track);
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssetIfDirty(timeline);
        Debug.Log("Skill camera environment connected: position/recenter signals, activation/noise clips, external camera rigs.");
    }

    private static void SetUpScene(Scene scene, SignalAsset position, SignalAsset recenter,
        TimelineActivationTrack activation, SkillCameraSignalTrack signals, CameraNoiseTrack noise,
        System.Collections.Generic.IEnumerable<TrackAsset> obsoleteTracks, PropertyName[] obsoleteReferences)
    {
        var players = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerCore>(true)).ToArray();
        var cameras = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CinemachineCamera>(true)).ToArray();
        var brain = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CinemachineBrain>(true)).FirstOrDefault();
        foreach (var player in players)
        {
            var director = SkillDirector(player.gameObject);
            ClearObsoleteBindings(director, obsoleteTracks, obsoleteReferences);
            var controller = Component<PlayerSkillCameraDirector>(director.gameObject);
            bool initializeOrigin = controller.PreviewOrigin == null;
            var tps = cameras.FirstOrDefault(camera => camera.GetComponent<CinemachineOrbitalFollow>() != null);
            if (tps == null || brain == null)
                throw new InvalidOperationException("TPS camera or CinemachineBrain missing in " + scene.path);
            var skill = controller.SkillCamera;
            if (skill == null || skill.transform.IsChildOf(player.transform))
                skill = cameras.FirstOrDefault(camera => camera.name == "Skill Camera" && !camera.transform.IsChildOf(player.transform));
            Transform origin;
            if (skill == null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath), scene);
                skill = rig.GetComponentInChildren<CinemachineCamera>(true);
                origin = rig.transform.Find("Preview Skill Origin");
                initializeOrigin = true;
            }
            else
            {
                var rig = skill.transform.parent;
                if (rig == null || rig.IsChildOf(player.transform))
                {
                    var rigObject = new GameObject("Player Skill Camera Rig");
                    SceneManager.MoveGameObjectToScene(rigObject, scene);
                    rig = rigObject.transform;
                    skill.transform.SetParent(rig, true);
                }
                origin = rig.Find("Preview Skill Origin");
                if (origin == null)
                {
                    origin = new GameObject("Preview Skill Origin").transform;
                    origin.SetParent(rig, false);
                }
            }
            PrepareCamera(skill);
            if (initializeOrigin)
                origin.SetPositionAndRotation(player.transform.position, Facing(player.gameObject).rotation);
            controller.Configure(skill, tps.GetComponent<CinemachineOrbitalFollow>(), brain,
                player.transform, Facing(player.gameObject), origin, position, recenter, activation);
            var receiver = Component<SignalReceiver>(director.gameObject);
            ConnectSignals(receiver, controller, position, recenter);
            var activeTarget = Component<GameObjectActivationTarget>(director.gameObject);
            activeTarget.Configure(skill.gameObject);
            var noiseTarget = Component<CinemachineCameraNoiseTarget>(director.gameObject);
            noiseTarget.Configure(skill.GetComponent<TimelineCameraNoiseExtension>());
            director.SetGenericBinding(activation, activeTarget);
            director.SetGenericBinding(noise, noiseTarget);
            director.SetGenericBinding(signals, receiver);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(activeTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(noiseTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            PrefabUtility.RecordPrefabInstancePropertyModifications(receiver);
        }
    }
    private static void ClearObsoleteBindings(PlayableDirector director,
        System.Collections.Generic.IEnumerable<TrackAsset> tracks, PropertyName[] references)
    {
        foreach (var track in tracks)
            director.ClearGenericBinding(track);
        foreach (var reference in references)
            director.ClearReferenceValue(reference);
    }

    private static void EnsureRigPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RigPath) != null)
            return;
        var source = Resources.FindObjectsOfTypeAll<CinemachineCamera>().FirstOrDefault(camera =>
            !EditorUtility.IsPersistent(camera) && camera.gameObject.scene.IsValid() && camera.name.StartsWith("Skill Camera", StringComparison.Ordinal));
        var root = new GameObject("Player Skill Camera Rig");
        try
        {
            var cameraObject = source != null ? Object.Instantiate(source.gameObject) : new GameObject("Skill Camera");
            cameraObject.name = "Skill Camera";
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
            var camera = Component<CinemachineCamera>(cameraObject);
            PrepareCamera(camera);
            new GameObject("Preview Skill Origin").transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, RigPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static void PrepareCamera(CinemachineCamera camera)
    {
        camera.Follow = null;
        camera.LookAt = null;
        camera.Priority.Enabled = true;
        camera.Priority.Value = 100;
        camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        Component<TimelineCameraNoiseExtension>(camera.gameObject);
        // Keep authored profiles/settings, while preventing a second, wall-clock noise source.
        foreach (var noise in camera.GetComponents<CinemachineBasicMultiChannelPerlin>())
            noise.enabled = false;
        foreach (var listener in camera.GetComponents<CinemachineImpulseListener>())
            listener.enabled = false;
        camera.gameObject.SetActive(false);
    }
    private static PlayableDirector SkillDirector(GameObject root) => root.GetComponentsInChildren<PlayableDirector>(true)
        .Single(director => AssetDatabase.GetAssetPath(director.playableAsset) == PlayerSkillImpactFrameSetup.TimelinePath);
    private static Transform Facing(GameObject player)
    {
        var rotator = player.GetComponent<PlayerRotator>();
        var property = rotator != null ? new SerializedObject(rotator).FindProperty("_modelTransform") : null;
        return property?.objectReferenceValue as Transform ?? player.transform;
    }
    private static T Component<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    private static SignalAsset SignalAt(string name) => AssetDatabase.LoadAssetAtPath<SignalAsset>(
        "Assets/05_Timeline Signals/Player/" + name + ".signal");
    private static SignalAsset Signal(string path)
    {
        var signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
        if (signal == null)
        {
            signal = ScriptableObject.CreateInstance<SignalAsset>();
            AssetDatabase.CreateAsset(signal, path);
        }
        return signal;
    }
    private static void AddMarker(SkillCameraSignalTrack track, SignalAsset asset, double time)
    {
        if (track.GetMarkers().OfType<SignalEmitter>().Any(marker => marker.asset == asset))
            return;
        var marker = track.CreateMarker<SignalEmitter>(time);
        marker.asset = asset;
        marker.retroactive = false;
        marker.emitOnce = false;
        EditorUtility.SetDirty(marker);
    }
    private static void ConnectSignals(SignalReceiver receiver, PlayerSkillCameraDirector controller,
        SignalAsset position, SignalAsset recenter)
    {
        for (int i = receiver.Count() - 1; i >= 0; i--)
        {
            var reaction = receiver.GetReactionAtIndex(i);
            if (reaction == null)
                continue;
            bool legacy = false;
            for (int call = 0; call < reaction.GetPersistentEventCount(); call++)
                legacy |= reaction.GetPersistentMethodName(call) == "CinemachineRemoveParent" ||
                    reaction.GetPersistentMethodName(call) == "CinemachineAddParent";
            if (legacy)
                receiver.Remove(receiver.GetSignalAssetAtIndex(i));
        }
        Connect(receiver, position, controller.PositionSkillCamera);
        Connect(receiver, recenter, controller.PlayerTPSCameraRecenter);
    }
    private static void Connect(SignalReceiver receiver, SignalAsset signal, UnityAction action)
    {
        var reaction = receiver.GetReaction(signal);
        if (reaction == null)
        {
            reaction = new UnityEvent();
            receiver.AddReaction(signal, reaction);
        }
        for (int i = 0; i < reaction.GetPersistentEventCount(); i++)
            if (reaction.GetPersistentTarget(i) == action.Target as Object && reaction.GetPersistentMethodName(i) == action.Method.Name)
                return;
        UnityEventTools.AddPersistentListener(reaction, action);
        EditorUtility.SetDirty(receiver);
    }
}
