using System;
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SkillCameraRuntimeVerification
{
    private const string Request = "Temp/SkillCameraRuntimeVerification.request";
    static SkillCameraRuntimeVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && File.Exists(Request))
                EditorApplication.delayCall += Run;
        };
    }
    [MenuItem("Tools/Timeline/Validate Skill Camera in Play Mode")]
    public static void RequestRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start verification from Edit mode.");
        File.WriteAllText(Request, "Run isolated camera runtime checks, then return to Edit mode.");
        EditorApplication.isPlaying = true;
    }

    private static void Run()
    {
        File.Delete(Request);
        var root = new GameObject("Skill Camera Runtime Verification");
        Scene scene = SceneManager.CreateScene("SkillCameraRuntimeVerification");
        SceneManager.MoveGameObjectToScene(root, scene);
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var position = ScriptableObject.CreateInstance<SignalAsset>();
        var recenter = ScriptableObject.CreateInstance<SignalAsset>();
        try
        {
            GameObject Child(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                return go;
            }
            var actor = Child("Actor").transform;
            var model = Child("Model").transform;
            model.SetParent(actor, false);
            model.rotation = Quaternion.Euler(0f, 90f, 0f);
            var anchor = Child("Camera Anchor").transform;
            anchor.SetParent(actor, false);
            anchor.localPosition = Vector3.up * 1.4f;
            var main = Child("Main");
            main.AddComponent<Camera>();
            var brain = main.AddComponent<CinemachineBrain>();
            brain.ChannelMask = OutputChannels.Channel15;
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.2f);
            var tps = Child("TPS").AddComponent<CinemachineCamera>();
            tps.OutputChannel = OutputChannels.Channel15;
            tps.Follow = anchor;
            tps.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
            var orbit = tps.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            orbit.HorizontalAxis.Value = -45f;
            orbit.HorizontalAxis.Recentering.Enabled = false;
            orbit.VerticalAxis.Value = 32f;
            orbit.VerticalAxis.Recentering.Enabled = false;
            tps.gameObject.AddComponent<CinemachineRotationComposer>();
            var input = tps.gameObject.AddComponent<CinemachineInputAxisController>();
            input.AutoEnableInputs = false;
            var skill = Child("Skill").AddComponent<CinemachineCamera>();
            skill.OutputChannel = OutputChannels.Channel15;
            skill.Priority.Enabled = true;
            skill.Priority.Value = 100;
            var extension = skill.gameObject.AddComponent<TimelineCameraNoiseExtension>();
            skill.gameObject.SetActive(false);
            var host = Child("Director");
            var director = host.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2d;
            var receiver = host.AddComponent<SignalReceiver>();
            var controller = host.AddComponent<PlayerSkillCameraDirector>();
            var activeTarget = host.AddComponent<GameObjectActivationTarget>();
            activeTarget.Configure(skill.gameObject);
            var noiseTarget = host.AddComponent<CinemachineCameraNoiseTarget>();
            noiseTarget.Configure(extension);
            var activation = timeline.CreateTrack<TimelineActivationTrack>();
            var activeClip = activation.CreateClip<TimelineActivationClip>();
            activeClip.start = 0.2d;
            activeClip.duration = 1.2d;
            director.SetGenericBinding(activation, activeTarget);
            var signals = timeline.CreateTrack<SkillCameraSignalTrack>();
            signals.CreateMarker<SignalEmitter>(0.1d).asset = position;
            signals.CreateMarker<SignalEmitter>(1d).asset = recenter;
            director.SetGenericBinding(signals, receiver);
            var noise = timeline.CreateTrack<CameraNoiseTrack>();
            var noiseClip = noise.CreateClip<CameraNoiseClip>();
            noiseClip.start = 0.3d;
            noiseClip.duration = 0.95d;
            director.SetGenericBinding(noise, noiseTarget);
            controller.Configure(skill, orbit, brain, actor, model, actor, position, recenter, activation);
            var positionReaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(positionReaction, controller.PositionSkillCamera);
            receiver.AddReaction(position, positionReaction);
            var recenterReaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(recenterReaction, controller.PlayerTPSCameraRecenter);
            receiver.AddReaction(recenter, recenterReaction);
            int frame = Time.frameCount;
            void Eval(double time)
            {
                director.time = time;
                director.Evaluate();
                brain.ManualUpdate(++frame, 1f / 60f);
            }

            controller.CaptureSkillStart(new Vector3(2f, 0f, 3f), model.rotation);
            director.Play();
            Eval(0.15d);
            Vector3 expected = new Vector3(-6f, 5f, 3f);
            Check(Vector3.Distance(skill.transform.position, expected) < 0.001f, "Runtime signals use the captured model-facing start pose");
            Check(!skill.gameObject.activeSelf, "Runtime placement happens before activation");
            Quaternion cameraHeading = skill.transform.rotation;
            Eval(0.21d);
            Eval(0.23d);
            Check(skill.gameObject.activeSelf && !input.enabled, "Observer activates and TPS input is held");
            actor.position += Vector3.right * 4f;
            Eval(0.47d);
            expected += Vector3.right * 4f;
            Check(Vector3.Distance(skill.transform.position, expected) < 0.001f, "Runtime camera follows the player's displacement");
            actor.position += Vector3.up * 0.5f;
            model.rotation = Quaternion.Euler(0f, 150f, 0f);
            controller.SendMessage("LateUpdate");
            expected += Vector3.up * 0.5f;
            Check(Vector3.Distance(skill.transform.position, expected) < 0.001f,
                "Camera follows late movement without another Timeline evaluation");
            Check(Quaternion.Angle(skill.transform.rotation, cameraHeading) < 0.001f,
                "Following preserves the skill-start viewing direction");
            Vector3 held = extension.Offset;
            Check(held.sqrMagnitude > 0f, "Runtime noise is applied");
            foreach (var playable in Roots(director))
                playable.SetSpeed(0d);
            director.playableGraph.Evaluate(0.1f);
            Check(Vector3.Distance(extension.Offset, held) < 0.001f, "Hit stop holds the exact noise phase");
            foreach (var playable in Roots(director))
                playable.SetSpeed(1d);
            controller.RecenterMode = PlayerSkillCameraDirector.RecenterModes.Smooth;
            controller.RecenterDuration = 0.2f;
            Eval(1.05d);
            Check(Mathf.Abs(Mathf.DeltaAngle(orbit.HorizontalAxis.Value, 150f)) > 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(orbit.HorizontalAxis.Value, -45f)) > 0.001f,
                "Runtime smooth recenter progresses toward the model heading at the signal");
            model.rotation = Quaternion.Euler(0f, -80f, 0f);
            Eval(1.3d);
            Check(Mathf.Abs(orbit.HorizontalAxis.Value - 150f) < 0.001f,
                "Runtime recenter captures current model heading at the marker, independently of root, anchor, and skill-start heading");
            Check(Vector3.Dot(Vector3.ProjectOnPlane(anchor.position - tps.State.GetFinalPosition(), Vector3.up).normalized,
                Quaternion.Euler(0f, 150f, 0f) * Vector3.forward) > 0.999f,
                "Evaluated TPS camera is behind the heading captured at the signal");
            Check(Mathf.Abs(orbit.VerticalAxis.Value - orbit.VerticalAxis.Center) < 0.001f, "Runtime vertical recenter completes");
            Check(!orbit.HorizontalAxis.Recentering.Enabled, "Runtime preserves auto-recenter preferences");
            Eval(1.41d);
            for (int i = 0; i < 20; i++)
                brain.ManualUpdate(++frame, 1f / 60f);
            controller.SendMessage("LateUpdate");
            Check(!skill.gameObject.activeSelf && input.enabled, "Return completes and restores TPS input");
            director.Stop();
            Check(extension.Offset == Vector3.zero, "Runtime stop clears noise");
            Vector3 stoppedPosition = skill.transform.position;
            actor.position += Vector3.right;
            controller.SendMessage("LateUpdate");
            Check(Vector3.Distance(skill.transform.position, stoppedPosition) < 0.001f,
                "Stopped skill no longer moves the external camera");

            model.rotation = Quaternion.Euler(0f, 180f, 0f);
            controller.CaptureSkillStart(Vector3.zero, model.rotation);
            director.Play();
            Eval(0.15d);
            Check(Vector3.Distance(skill.transform.position, new Vector3(0f, 5f, 8f)) < 0.001f,
                $"Next skill captures a fresh facing direction (actual: {skill.transform.position}, actor: {actor.position})");
            Eval(0.47d);
            controller.RecenterMode = PlayerSkillCameraDirector.RecenterModes.Instant;
            Eval(1.05d);
            Check(Mathf.Abs(Mathf.DeltaAngle(orbit.HorizontalAxis.Value, 180f)) < 0.001f,
                "Runtime instant recenter supports a heading at the horizontal axis wrap boundary");
            director.Pause();
            director.Stop();
            Check(!skill.gameObject.activeSelf && input.enabled && extension.Offset == Vector3.zero,
                "Paused skill cancellation restores external camera and input");
            File.WriteAllText("Temp/SkillCameraRuntimeVerification.result", "PASS: captured direction, player follow, late movement, stop cleanup, input lock/restore, timeline hit stop, smooth recenter, return, replay, cancellation");
            Debug.Log("Skill camera runtime verification passed.");
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/SkillCameraRuntimeVerification.result", "FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(position);
            Object.DestroyImmediate(recenter);
            EditorApplication.isPlaying = false;
        }
    }

    private static System.Collections.Generic.IEnumerable<Playable> Roots(PlayableDirector director)
    {
        for (int i = 0; i < director.playableGraph.GetRootPlayableCount(); i++)
            yield return director.playableGraph.GetRootPlayable(i);
    }
    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
