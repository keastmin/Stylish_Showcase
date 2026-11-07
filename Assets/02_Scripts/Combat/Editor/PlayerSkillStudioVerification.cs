using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Isolated checks for E authoring and its real gameplay bindings. Never saves authored assets.</summary>
public static class PlayerSkillStudioVerification
{
    private const string ReportPath = "Temp/PlayerSkillStudioVerification.result";
    private const string TimelinePath = "Assets/04_Timelines/Player/SkillTimeline.playable";
    private static readonly List<string> Evidence = new();
    public static string LastReport { get; private set; }

    [MenuItem("Tools/Character/Player/Validate E Skill Studio")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("E Skill Studio verification runs in Edit Mode.");
        Evidence.Clear();
        Directory.CreateDirectory("Temp");
        try
        {
            ValidateIsolatedBehavior();
            ValidateAuthoredBindings();
            LastReport = $"PASS: E Skill Studio ({Evidence.Count} checks).";
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            Debug.Log(LastReport);
        }
        catch (Exception exception)
        {
            LastReport = "FAIL: E Skill Studio: " + exception;
            File.WriteAllText(ReportPath, LastReport + "\n" + string.Join("\n", Evidence));
            throw;
        }
    }

    private static void ValidateIsolatedBehavior()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var signal = ScriptableObject.CreateInstance<SignalAsset>();
        PlayerSkillCameraDirector cameraController = null;
        try
        {
            GameObject Make(string name)
            {
                var value = new GameObject("E Studio Verification / " + name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(value, scene);
                return value;
            }
            var host = Make("Director");
            var director = host.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            var settings = host.AddComponent<PlayerSkillSequenceSettings>();
            var activation = timeline.CreateTrack<TimelineActivationTrack>();
            var clip = activation.CreateClip<TimelineActivationClip>();
            clip.start = .5d;
            clip.duration = 2d;
            timeline.durationMode = TimelineAsset.DurationMode.BasedOnClips;

            settings.UseTimelineDuration = true;
            Near(settings.GetContentEndTime(director), 2.5d, "Auto sequence end follows the last clip end");
            clip.duration = 4d;
            Near(settings.GetContentEndTime(director), 4.5d, "Extending a clip extends the automatic skill duration");
            settings.RecoveryDuration = .3f;
            Near(settings.GetEndTime(director), 4.8d, "Recovery begins after the automatic content end");
            settings.UseTimelineDuration = false;
            settings.EndTime = 1.25f;
            Near(settings.GetContentEndTime(director), 1.25d, "Manual content cutoff is independent of the last clip");
            Near(settings.GetEndTime(director), 1.55d, "Manual cutoff retains its separate recovery duration");
            settings.EndTime = 9f;
            Near(settings.GetContentEndTime(director), 4.5d, "Manual cutoff cannot run beyond the available Timeline");
            settings.RecoveryDuration = -1f;
            Near(settings.RecoveryDuration, 0d, "Negative recovery cannot delay or reverse completion");
            settings.EndTime = float.NaN;
            settings.RecoveryDuration = float.PositiveInfinity;
            Near(settings.GetEndTime(director), 2.2d, "Nonfinite timing input falls back to a finite duration");
            Near(PlayerSkillSequenceSettings.TimelineDuration(null), 2.2d, "Missing director has a finite compatibility duration");

            var actor = Make("Actor");
            var model = Make("Model");
            model.transform.SetParent(actor.transform, false);
            var origin = Make("Origin");
            origin.transform.SetPositionAndRotation(new Vector3(2, 0, 3), Quaternion.Euler(0, 90, 0));
            var camera = Make("Camera").AddComponent<CinemachineCamera>();
            camera.transform.SetPositionAndRotation(new Vector3(8, 9, 10), Quaternion.Euler(10, 25, 3));
            var lens = camera.Lens;
            lens.FieldOfView = 47f;
            camera.Lens = lens;
            camera.gameObject.SetActive(false);
            cameraController = host.AddComponent<PlayerSkillCameraDirector>();
            cameraController.Configure(camera, null, null, actor.transform, model.transform, origin.transform,
                signal, null, activation);
            PlayerSkillCameraPose fallback = cameraController.EvaluateMotion(.75d);
            Near(fallback.offset, new Vector3(0, 5, -8), "Motion disabled preserves the original camera offset");
            Near(fallback.rotation, Quaternion.Euler(16, 0, 0), "Motion disabled preserves the original viewing angle");
            Near(fallback.fieldOfView, 47d, "Motion disabled preserves the existing camera lens");

            var first = new PlayerSkillCameraKey { time = 0, offset = Vector3.zero, euler = new Vector3(0, 350, 0),
                fieldOfView = 40, interpolation = PlayerSkillCameraInterpolation.Linear };
            var last = new PlayerSkillCameraKey { time = 2, offset = new Vector3(10, 4, -2),
                euler = new Vector3(0, 10, 0), fieldOfView = 80 };
            cameraController.MotionKeys.Add(last);
            cameraController.MotionKeys.Add(first); // Deliberately unsorted, as during drag editing.
            cameraController.MotionEnabled = true;
            var midpoint = cameraController.EvaluateMotion(1d);
            Near(midpoint.offset, new Vector3(5, 2, -1), "Unsorted camera keys interpolate position by time");
            Near(midpoint.rotation, Quaternion.identity, "Rotation crosses 360 degrees by the shortest arc");
            Near(midpoint.fieldOfView, 60d, "Camera lens interpolates with its movement");
            Near(cameraController.EvaluateMotion(-1d).offset, first.offset, "Before the first key the first pose is held");
            Near(cameraController.EvaluateMotion(3d).offset, last.offset, "After the last key the last pose is held");
            Near(cameraController.EvaluateMotion(.5d).offset, last.offset * .25f, "Linear movement uses the outgoing key interpolation");
            first.interpolation = PlayerSkillCameraInterpolation.SmoothStep;
            Near(cameraController.EvaluateMotion(.5d).offset, last.offset * .15625f, "Smooth movement eases the same authored interval");
            cameraController.EvaluateMotion(1.8d);
            Near(cameraController.EvaluateMotion(.5d).offset, last.offset * .15625f, "Backward and repeated sampling are deterministic");
            var duplicate = new PlayerSkillCameraKey { time = 2, offset = new Vector3(99, 3, 1), fieldOfView = 55 };
            cameraController.MotionKeys.Add(duplicate);
            Near(cameraController.EvaluateMotion(2d).offset, duplicate.offset, "Duplicate key times consistently select the last list entry");
            cameraController.MotionKeys.Remove(duplicate);
            cameraController.MotionKeys.Clear();
            Near(cameraController.EvaluateMotion(1d).offset, fallback.offset, "Enabled motion with no keys safely preserves original placement");
            cameraController.MotionKeys.Add(first);
            cameraController.MotionKeys.Add(last);
            first.interpolation = PlayerSkillCameraInterpolation.Linear;

            var signals = timeline.CreateTrack<SkillCameraSignalTrack>();
            signals.CreateMarker<SignalEmitter>(0d).asset = signal;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            void Evaluate(double time)
            {
                director.time = time;
                cameraController.EvaluateTimeline(signals, time);
            }
            Evaluate(1d);
            Vector3 expectedPosition = origin.transform.position + origin.transform.rotation * midpoint.offset;
            Near(camera.transform.position, expectedPosition, "Actual camera samples keys in the skill facing coordinate system");
            Near(camera.Lens.FieldOfView, 60d, "Actual camera receives the interpolated lens");
            Vector3 displacement = new(3, 0, 2);
            actor.transform.position += displacement;
            Evaluate(1d);
            Near(camera.transform.position, expectedPosition + displacement, "Following adds actual player displacement exactly once");
            cameraController.FollowPlayerMovement = false;
            Evaluate(1d);
            Near(camera.transform.position, expectedPosition, "Disabling follow keeps the camera anchored to the cast origin");
            cameraController.EndTimelineSession();
            Near(camera.transform.position, originalPosition, "Ending preview restores the original camera position");
            Near(camera.transform.rotation, originalRotation, "Ending preview restores the original camera rotation");
            Near(camera.Lens.FieldOfView, 47d, "Ending preview restores the original camera lens");
        }
        finally
        {
            if (cameraController != null) cameraController.EndTimelineSession();
            EditorSceneManager.ClosePreviewScene(scene);
            DestroyScratchTimeline(timeline);
            Object.DestroyImmediate(signal);
        }
    }

    private static void ValidateAuthoredBindings()
    {
        var inputs = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input Actions/PlayerInputAction.inputactions");
        Check(inputs != null, "Player input asset exists");
        Check(inputs.FindAction("Skill")?.bindings.Any(binding => binding.path == "<Keyboard>/e") == true,
            "E remains bound to the Timeline Skill action");
        Check(inputs.FindAction("Ultimate")?.bindings.Any(binding => binding.path == "<Keyboard>/q") == true,
            "Q remains bound to the independent Ultimate action");
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        Check(timeline != null, "The real E Timeline exists");
        var tracks = timeline.GetOutputTracks().ToArray();
        var hitTracks = tracks.OfType<PlayerSkillHitTrack>().ToArray();
        Check(hitTracks.Length > 0 && hitTracks.Any(track => track.GetMarkers().OfType<PlayerSkillHitMarker>().Any()),
            "The real E Timeline contains authored damage markers");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/08_Prefabs/Combat/Player.prefab");
        Check(prefab != null, "The gameplay player prefab exists");
        var prefabReceivers = prefab.GetComponentsInChildren<PlayerSkillAttackReceiver>(true);
        Check(prefabReceivers.Length > 0, "Player prefab retains the E damage receiver");
        foreach (var receiver in prefabReceivers)
            Check(receiver.GetComponent<PlayerSkillSequenceSettings>() != null, "Player prefab connects editable E sequence timing");

        foreach (string path in PlayerSkillCameraSetup.ScenePaths)
        {
            byte[] sceneBefore = File.ReadAllBytes(path);
            Scene scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var receivers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerSkillAttackReceiver>(true)).ToArray();
                Check(receivers.Length > 0, "Scene has the real E damage receiver: " + path);
                foreach (var receiver in receivers) ValidateReceiver(receiver, timeline, tracks, hitTracks, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Check(sceneBefore.SequenceEqual(File.ReadAllBytes(path)), "Verification preserves the authored scene file: " + path);
        }
    }

    private static void ValidateReceiver(PlayerSkillAttackReceiver receiver, TimelineAsset timeline, TrackAsset[] tracks,
        PlayerSkillHitTrack[] hitTracks, string path)
    {
        var director = receiver.GetComponent<PlayableDirector>();
        Check(director != null && director.playableAsset == timeline, "Scene receiver uses the real E Timeline: " + path);
        var settings = receiver.GetComponent<PlayerSkillSequenceSettings>();
        double endTime = settings != null ? settings.GetEndTime(director) : double.NaN;
        Check(endTime > 0 && !double.IsInfinity(endTime), "Scene has finite editable sequence timing: " + path);
        Check(new SerializedObject(receiver).FindProperty("_attackContainer").objectReferenceValue != null,
            "Damage stays connected to the player combat pipeline: " + path);
        foreach (var track in hitTracks)
        {
            Check(director.GetGenericBinding(track) == receiver, "Damage track is bound to the scene receiver: " + path);
            foreach (var marker in track.GetMarkers().OfType<PlayerSkillHitMarker>())
            {
                var field = receiver.GetDamageField(marker.DamageFieldNumber);
                Check(field != null && field.Hitbox != null, "Authored marker resolves to a real hitbox: " + marker.name);
                Check(field.Hitbox is BoxCollider || field.Hitbox is SphereCollider || field.Hitbox is CapsuleCollider,
                    "Authored marker uses a supported physics query shape: " + marker.name);
                Check(!field.Hitbox.enabled, "Authored hitbox remains query-only: " + field.Hitbox.name);
            }
        }
        var camera = receiver.GetComponent<PlayerSkillCameraDirector>();
        Check(camera != null && camera.SkillCamera != null && camera.TPSOrbit != null && camera.PreviewOrigin != null,
            "E camera and return camera are connected: " + path);
        var target = receiver.GetComponent<GameObjectActivationTarget>();
        Check(target != null && camera.ActivationTrack != null && target.Target == camera.SkillCamera.gameObject &&
              director.GetGenericBinding(camera.ActivationTrack) == target, "Camera activation remains connected: " + path);
        foreach (var track in tracks.OfType<ControlTrack>())
            foreach (var clip in track.GetClips())
                if (clip.asset is ControlPlayableAsset control)
                    Check(control.prefabGameObject != null || control.sourceGameObject.Resolve(director) != null,
                        "VFX control clip resolves to its authored object: " + track.name + "/" + clip.displayName);
    }

    private static void DestroyScratchTimeline(TimelineAsset timeline)
    {
        if (timeline == null) return;
        foreach (var track in timeline.GetOutputTracks().ToArray())
        {
            foreach (var clip in track.GetClips().ToArray()) if (clip.asset != null) Object.DestroyImmediate(clip.asset);
            foreach (var marker in track.GetMarkers().ToArray()) Object.DestroyImmediate((Object)marker);
            Object.DestroyImmediate(track);
        }
        Object.DestroyImmediate(timeline);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Evidence.Add("PASS: " + message);
    }
    private static void Near(double actual, double expected, string message)
        => Check(Math.Abs(actual - expected) < .001d, message + $" ({actual:0.####})");
    private static void Near(Vector3 actual, Vector3 expected, string message)
        => Check(Vector3.Distance(actual, expected) < .001f, message + " (" + actual + ")");
    private static void Near(Quaternion actual, Quaternion expected, string message)
        => Check(Quaternion.Angle(actual, expected) < .02f, message);
}
