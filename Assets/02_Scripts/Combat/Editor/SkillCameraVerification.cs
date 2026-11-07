using System;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public static class SkillCameraVerification
{
    [MenuItem("Tools/Timeline/Validate Skill Camera")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run editor preview verification in Edit mode.");
        PlayerSkillCameraDirector.RestoreAllPreviews();
        TimelineActivationRegistry.RestoreAll();
        var scene = EditorSceneManager.NewPreviewScene();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var positionSignal = ScriptableObject.CreateInstance<SignalAsset>();
        var recenterSignal = ScriptableObject.CreateInstance<SignalAsset>();
        try
        {
            GameObject Make(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            var actor = Make("Actor");
            var model = Make("Model");
            model.transform.SetParent(actor.transform, false);
            model.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var anchor = Make("Camera Anchor");
            anchor.transform.SetParent(actor.transform, false);
            anchor.transform.localPosition = Vector3.up * 1.4f;
            var origin = Make("Preview Origin");
            origin.transform.SetPositionAndRotation(new Vector3(2f, 0f, 3f), model.transform.rotation);
            var main = Make("Main Camera");
            main.AddComponent<Camera>();
            var brain = main.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.2f);
            var tps = Make("TPS").AddComponent<CinemachineCamera>();
            tps.Follow = anchor.transform;
            var orbit = tps.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            orbit.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
            orbit.HorizontalAxis.Value = -45f;
            orbit.HorizontalAxis.Recentering.Enabled = false;
            orbit.VerticalAxis.Value = 32f;
            orbit.VerticalAxis.Recentering.Enabled = false;
            tps.gameObject.AddComponent<CinemachineRotationComposer>();
            var skill = Make("Skill Camera").AddComponent<CinemachineCamera>();
            skill.Priority.Value = 100;
            skill.Priority.Enabled = true;
            skill.transform.position = new Vector3(10f, 10f, 10f);
            var extension = skill.gameObject.AddComponent<TimelineCameraNoiseExtension>();
            skill.gameObject.SetActive(false);
            var host = Make("Skill Director");
            var director = host.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2d;
            var receiver = host.AddComponent<SignalReceiver>();
            var controller = host.AddComponent<PlayerSkillCameraDirector>();
            var activationTarget = host.AddComponent<GameObjectActivationTarget>();
            activationTarget.Configure(skill.gameObject);
            var noiseTarget = host.AddComponent<CinemachineCameraNoiseTarget>();
            noiseTarget.Configure(extension);
            var activation = timeline.CreateTrack<TimelineActivationTrack>();
            var cameraClip = activation.CreateClip<TimelineActivationClip>();
            cameraClip.start = 0.2d;
            cameraClip.duration = 1.2d;
            director.SetGenericBinding(activation, activationTarget);
            var signals = timeline.CreateTrack<SkillCameraSignalTrack>();
            signals.CreateMarker<SignalEmitter>(0.1d).asset = positionSignal;
            signals.CreateMarker<SignalEmitter>(1d).asset = recenterSignal;
            director.SetGenericBinding(signals, receiver);
            var noise = timeline.CreateTrack<CameraNoiseTrack>();
            var noiseClip = noise.CreateClip<CameraNoiseClip>();
            noiseClip.start = 0.3d;
            noiseClip.duration = 0.95d;
            director.SetGenericBinding(noise, noiseTarget);
            controller.Configure(skill, orbit, brain, actor.transform, model.transform, origin.transform,
                positionSignal, recenterSignal, activation);
            Vector3 baselinePosition = skill.transform.position;

            Eval(director, 0.05d);
            Check(!skill.gameObject.activeSelf, "Before activation is OFF");
            Near(skill.transform.position, baselinePosition, "Before positioning signal preserves original pose");
            Eval(director, 0.15d);
            Check(!skill.gameObject.activeSelf, "Positioning occurs while skill camera is still OFF");
            Vector3 expected = origin.transform.position + origin.transform.rotation * new Vector3(0f, 5f, -8f);
            Near(skill.transform.position, expected, "Position uses fixed preview origin and facing direction");
            Eval(director, 0.25d);
            Check(skill.gameObject.activeSelf, "Activation clip enables external camera");
            Check(controller.PreviewSkillWeight(0.25d) > 0f && controller.PreviewSkillWeight(0.25d) < 1f,
                "Entry blend is evaluated from Timeline time");
            Near(main.transform.position, CameraState.Lerp(tps.State, skill.State, controller.PreviewSkillWeight(0.25d)).GetFinalPosition(),
                "Entry blend preview reaches the actual output camera");
            actor.transform.position += Vector3.forward * 4f;
            Eval(director, 0.4d);
            expected += Vector3.forward * 4f;
            Near(skill.transform.position, expected, "Preview camera follows actor displacement");
            Check(Mathf.Abs(extension.Offset.z) > 0.00001f, "Noise has a visible Z signal");
            Near(extension.Offset.x, 0f, "Noise does not move sideways");
            Near(extension.Offset.y, 0f, "Noise does not move vertically");
            Vector3 sample = extension.Offset;
            Near(main.transform.position, skill.State.GetFinalPosition(), "Noise preview reaches the actual output camera");
            Eval(director, 0.6d);
            actor.transform.position -= Vector3.forward * 4f;
            Eval(director, 0.4d);
            Near(skill.transform.position, expected - Vector3.forward * 4f,
                "Backward preview movement restores the matching camera position");
            actor.transform.position += Vector3.forward * 4f;
            Eval(director, 0.4d);
            Near(extension.Offset, sample, "Backward scrubbing reproduces the same noise sample");
            var settings = (CameraNoiseClip)noiseClip.asset;
            double peak = noiseClip.start + noiseClip.duration * settings.FinisherPosition + settings.FinisherDuration * 0.2d;
            Eval(director, peak);
            Check(Mathf.Abs(extension.Offset.z) > settings.Amplitude * 2f, "Final impact is stronger than continuous noise");
            Vector3 impact = extension.Offset;
            Eval(director, peak);
            Near(extension.Offset, impact, "Repeated evaluation does not accumulate impacts");
            Eval(director, 1.1d);
            Near(orbit.HorizontalAxis.Value, 90f, "TPS recenters behind Model while root and Follow anchor remain at zero yaw");
            Check(Vector3.Dot(Vector3.ProjectOnPlane(anchor.transform.position - tps.State.GetFinalPosition(), Vector3.up).normalized,
                model.transform.forward) > 0.999f, "Evaluated TPS camera is behind the model");
            Near(orbit.VerticalAxis.Value, orbit.VerticalAxis.Center, "TPS vertical axis returns to center");
            Check(!orbit.HorizontalAxis.Recentering.Enabled, "Manual recenter preserves disabled automatic recenter setting");
            model.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            Eval(director, 1.1d);
            Near(orbit.HorizontalAxis.Value, -90f, "Preview resamples a changed model heading after the recenter marker");
            model.transform.rotation = Quaternion.Euler(20f, 170f, 0f);
            Eval(director, 1.1d);
            Near(orbit.HorizontalAxis.Value, 170f, "Recenter uses current horizontal facing rather than skill-start facing or model pitch");
            model.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Eval(director, 0.9d);
            Near(orbit.HorizontalAxis.Value, -45f, "Backward seek before recenter restores original horizontal axis");
            Near(orbit.VerticalAxis.Value, 32f, "Backward seek before recenter restores original vertical axis");
            controller.RecenterMode = PlayerSkillCameraDirector.RecenterModes.Smooth;
            controller.RecenterDuration = 0.2f;
            Eval(director, 1.05d);
            Check(orbit.HorizontalAxis.Value > -45f && orbit.HorizontalAxis.Value < 90f, "Smooth recenter has an intermediate pose");
            Eval(director, 1.3d);
            Near(orbit.HorizontalAxis.Value, 90f, "Smooth recenter completes before camera ends");
            Near(extension.Offset, Vector3.zero, "Noise ends independently of camera activation");
            Eval(director, cameraClip.end);
            Check(!skill.gameObject.activeSelf, "Exact camera clip end switches OFF");
            Check(controller.PreviewSkillWeight(1.5d) > 0f && controller.PreviewSkillWeight(1.5d) < 1f,
                "Return blend remains visible after activation ends");
            Near(controller.PreviewSkillWeight(1.65d), 0f, "Return blend completes");

            Eval(director, 0.4d);
            director.Pause();
            Near(extension.Offset, sample, "Pause holds current noise sample");
            director.Stop();
            Near(extension.Offset, Vector3.zero, "Stop clears noise requests");
            Check(!skill.gameObject.activeSelf, "Stop restores inactive camera baseline");
            Near(skill.transform.position, baselinePosition, "Stop restores camera preview pose");
            Near(orbit.HorizontalAxis.Value, -45f, "Stop restores TPS axis baseline");

            Eval(director, 0.4d);
            PlayerSkillCameraDirector.SuspendPreviewsForSave();
            TimelineActivationRegistry.SuspendForSave();
            Near(skill.transform.position, baselinePosition, "Save suspends placement preview");
            Check(!skill.gameObject.activeSelf, "Save serializes original camera activation");
            TimelineActivationRegistry.ResumeAfterSave();
            PlayerSkillCameraDirector.ResumePreviewsAfterSave();
            Near(skill.transform.position, expected - Vector3.forward * 4f, "New preview session resumes after save");
            Check(skill.gameObject.activeSelf, "Activation resumes after save");
            director.Stop();

            Eval(director, 0.4d);
            director.ClearGenericBinding(noise);
            Eval(director, 0.4d);
            Near(extension.Offset, Vector3.zero, "Clearing noise binding releases old camera");
            director.SetGenericBinding(noise, noiseTarget);
            director.RebuildGraph();
            Eval(director, 0.4d);
            director.enabled = false;
            foreach (var guard in host.GetComponents<TimelineActivationLifetime>())
                guard.SendMessage("LateUpdate");
            controller.SendMessage("LateUpdate");
            Check(!skill.gameObject.activeSelf, "Disabling paused director restores external camera");
            Near(extension.Offset, Vector3.zero, "Disabling paused director clears noise");
            director.enabled = true;
            director.Stop();
            ValidateScenes();
            File.WriteAllText("Temp/SkillCameraVerification.result", "PASS: placement, player follow, backward movement, activation, noise, finisher, recenter, scrubbing, blends, lifecycle, saves, scene bindings");
            Debug.Log("Skill camera verification passed.");
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/SkillCameraVerification.result", "FAIL: " + exception);
            throw;
        }
        finally
        {
            PlayerSkillCameraDirector.RestoreAllPreviews();
            TimelineActivationRegistry.RestoreAll();
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(positionSignal);
            Object.DestroyImmediate(recenterSignal);
        }
    }

    private static void ValidateScenes()
    {
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(PlayerSkillImpactFrameSetup.TimelinePath);
        Check(timeline.GetOutputTracks().OfType<TimelineActivationTrack>().Count(track =>
            track.name == "Skill Camera" || track.name.StartsWith("Skill Camera (", StringComparison.Ordinal)) == 1,
            "Skill timeline contains exactly one camera activation track");
        Check(!timeline.GetOutputTracks().OfType<ControlTrack>().Any(track => track.name == "Skill Camera" || track.name == "Camera 1"),
            "Legacy camera control track is replaced");
        foreach (string path in PlayerSkillCameraSetup.ScenePaths)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerSkillCameraDirector>(true)).ToArray();
                Check(controllers.Length > 0, "Scene contains a bound skill camera controller: " + path);
                foreach (var controller in controllers)
                {
                    Check(controller.SkillCamera != null && controller.TPSOrbit != null && controller.PreviewOrigin != null,
                        "Scene camera bindings: " + path);
                    var player = controller.GetComponentInParent<PlayerCore>();
                    Check(player != null && !controller.SkillCamera.transform.IsChildOf(player.transform), "Skill camera is outside Player: " + path);
                    Check(!controller.SkillCamera.gameObject.activeSelf, "Scene camera saved OFF: " + path);
                    Check(controller.GetComponent<GameObjectActivationTarget>().Target == controller.SkillCamera.gameObject,
                        "Activation resource matches scene camera: " + path);
                    Check(controller.GetComponent<CinemachineCameraNoiseTarget>().Extension == controller.SkillCamera.GetComponent<TimelineCameraNoiseExtension>(),
                        "Noise binding matches scene camera: " + path);
                    Check(controller.GetComponent<PlayableDirector>().GetGenericBinding(controller.ActivationTrack) ==
                        controller.GetComponent<GameObjectActivationTarget>(), "Activation track binding matches controller: " + path);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
    private static void Eval(PlayableDirector director, double time)
    {
        director.time = time;
        director.Evaluate();
        PlayerSkillCameraDirector.RefreshPreviewOutputs();
    }
    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
    private static void Near(float actual, float expected, string description) => Check(Mathf.Abs(actual - expected) < 0.001f,
        description + " (actual=" + actual + ", expected=" + expected + ")");
    private static void Near(Vector3 actual, Vector3 expected, string description) => Check(Vector3.Distance(actual, expected) < 0.001f,
        description + " (actual=" + actual + ", expected=" + expected + ")");
}
