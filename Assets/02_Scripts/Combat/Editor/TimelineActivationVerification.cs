using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public static class TimelineActivationVerification
{
    private const string ResultPath = "Temp/TimelineActivationVerification.result";

    [MenuItem("Tools/Timeline/Validate Activation Timeline")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run activation verification in Edit mode.");

        TimelineActivationRegistry.RestoreAll();
        var scene = EditorSceneManager.NewPreviewScene();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        ScriptableRendererData renderer = null;
        ScriptableRendererFeature feature = null;
        ScriptableRendererFeature otherFeature = null;
        try
        {
            var sourceRenderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PlayerSkillImpactFrameSetup.RendererPath);
            var sourceFeature = sourceRenderer.rendererFeatures.Single(item => item != null && item.name == "Impact Frame");
            string rendererBefore = File.ReadAllText(PlayerSkillImpactFrameSetup.RendererPath);
            string materialPath = AssetDatabase.GetAssetPath(((FullScreenPassRendererFeature)sourceFeature).passMaterial);
            string materialBefore = File.ReadAllText(materialPath);
            renderer = Object.Instantiate(sourceRenderer);
            renderer.rendererFeatures.Clear();
            feature = Object.Instantiate(sourceFeature);
            otherFeature = Object.Instantiate(sourceFeature);
            renderer.rendererFeatures.Add(feature);
            renderer.rendererFeatures.Add(otherFeature);
            feature.SetActive(false);
            otherFeature.SetActive(true);

            var root = new GameObject("Activation Verification");
            SceneManager.MoveGameObjectToScene(root, scene);
            var target = root.AddComponent<RendererFeatureActivationTarget>();
            target.Configure(renderer, feature);
            var director = root.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2d;
            var track = timeline.CreateTrack<TimelineActivationTrack>();
            var clip = track.CreateClip<TimelineActivationClip>();
            Near(clip.duration, 2d / timeline.editorSettings.frameRate, "New clips use two timeline frames");
            clip.start = 0.5d;
            clip.duration = 0.1d;
            director.SetGenericBinding(track, target);

            Evaluate(director, 0.49d);
            Check(!feature.isActive, "Before clip is OFF");
            Evaluate(director, clip.start);
            Check(feature.isActive, "Exact start is ON");
            Evaluate(director, 0.55d);
            Check(feature.isActive, "Inside clip is ON");
            Evaluate(director, clip.end);
            Check(!feature.isActive, "Exact end is OFF");
            Evaluate(director, 0.55d);
            Evaluate(director, 0.49d);
            Check(!feature.isActive, "Backward scrubbing releases ON");

            var overlap = track.CreateClip<TimelineActivationClip>();
            overlap.start = 0.55d;
            overlap.duration = 0.1d;
            director.RebuildGraph();
            Evaluate(director, 0.6d);
            Check(feature.isActive, "Overlapping clip remains ON after first ends");
            Evaluate(director, 0.65d);
            Check(!feature.isActive, "Overlap ends without leaking activation");

            var secondTrack = timeline.CreateTrack<TimelineActivationTrack>();
            var secondClip = secondTrack.CreateClip<TimelineActivationClip>();
            secondClip.start = 0.6d;
            secondClip.duration = 0.1d;
            director.SetGenericBinding(secondTrack, target);
            director.RebuildGraph();
            Evaluate(director, 0.67d);
            Check(feature.isActive, "Two tracks merge requests instead of last-writer wins");
            Evaluate(director, 0.7d);
            Check(!feature.isActive, "Two tracks return to OFF");

            director.Play();
            Evaluate(director, 0.55d);
            director.Pause();
            Check(feature.isActive, "Pause holds current activation");
            director.Stop();
            Check(!feature.isActive, "Stop after pause restores baseline");
            director.Play();
            Evaluate(director, 0.55d);
            for (int i = 0; i < director.playableGraph.GetRootPlayableCount(); i++)
                director.playableGraph.GetRootPlayable(i).SetSpeed(0d);
            director.playableGraph.Evaluate(0.1f);
            Check(feature.isActive, "Zero-speed hit stop holds activation");
            director.Stop();
            Check(!feature.isActive, "Hit-stop interruption restores baseline");

            Evaluate(director, 0.55d);
            director.Pause();
            director.enabled = false;
            foreach (var guard in root.GetComponents<TimelineActivationLifetime>())
                guard.SendMessage("LateUpdate");
            Check(!feature.isActive, "Disabling a paused Director releases requests");
            director.enabled = true;
            director.Stop();
            Evaluate(director, 0.55d);
            root.SetActive(false);
            Check(!feature.isActive, "Disabling the owner restores baseline");
            root.SetActive(true);
            director.Stop();

            feature.SetActive(true);
            Evaluate(director, 0.1d);
            Check(!feature.isActive, "Empty intervals are OFF even with an originally ON resource");
            director.Stop();
            Check(feature.isActive, "Cleanup restores an originally ON resource");
            feature.SetActive(false);

            Evaluate(director, 0.55d);
            director.ClearGenericBinding(track);
            Evaluate(director, 0.55d);
            Check(!feature.isActive, "Removing a binding releases its request");
            director.SetGenericBinding(track, target);
            Evaluate(director, 0.55d);
            target.Configure(renderer, otherFeature);
            Check(!feature.isActive, "Rebinding restores the old feature");
            Evaluate(director, 0.1d);
            Check(!otherFeature.isActive, "Rebound feature follows empty intervals");
            director.Stop();
            Check(otherFeature.isActive, "Rebound feature restores its own original state");
            target.Configure(renderer, feature);

            Evaluate(director, 0.55d);
            director.RebuildGraph();
            Check(!feature.isActive, "Graph rebuild releases old ownership");
            Evaluate(director, 0.55d);
            TimelineActivationRegistry.SuspendForSave();
            Check(!feature.isActive, "Saving serializes the original state");
            TimelineActivationRegistry.ResumeAfterSave();
            Check(feature.isActive, "Paused preview requests survive saving");
            TimelineActivationRegistry.RestoreAll();
            Check(!feature.isActive, "Editor shutdown/reload cleanup restores baseline");
            director.Stop();

            var siblingObject = new GameObject("Second Adapter");
            SceneManager.MoveGameObjectToScene(siblingObject, scene);
            var sibling = siblingObject.AddComponent<RendererFeatureActivationTarget>();
            sibling.Configure(renderer, feature);
            var firstOwner = new object();
            var secondOwner = new object();
            target.SetRequestedActive(firstOwner, true);
            sibling.SetRequestedActive(secondOwner, true);
            target.enabled = false;
            Check(feature.isActive, "Another adapter's ON survives the first adapter disabling");
            sibling.ReleaseOwner(secondOwner);
            Check(!feature.isActive, "Last shared-resource owner restores baseline");
            target.enabled = true;

            secondTrack.muted = true;
            track.muted = true;
            director.RebuildGraph();
            Evaluate(director, 0.55d);
            Check(!feature.isActive, "Muted tracks do not activate the resource");
            track.muted = false;
            timeline.DeleteClip(overlap);
            clip.start = 43d / 60d;
            clip.duration = 2d / 60d;
            director.RebuildGraph();
            foreach (int fps in new[] { 30, 60, 120 })
            {
                int visibleSamples = 0;
                for (int i = 0; i < fps; i++)
                {
                    double time = (double)i / fps;
                    Evaluate(director, time);
                    bool expected = time >= clip.start && time < clip.end;
                    Check(feature.isActive == expected, $"{fps} fps sampled interval at {time:F6}");
                    if (feature.isActive)
                        visibleSamples++;
                }
                Check(visibleSamples > 0, $"Two-frame example appears at {fps} fps");
                director.Stop();
            }

            var sourceTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(PlayerSkillImpactFrameSetup.TimelinePath);
            var sourceTrack = sourceTimeline.GetOutputTracks().OfType<TimelineActivationTrack>()
                .Single(item => item.name == "Impact Frame");
            Check(sourceTrack.GetClips().Any(), "SkillTimeline contains editable activation clips");
            foreach (string scenePath in new[] { "Assets/01_Scenes/DemoScene 1.unity", "Assets/01_Scenes/CombatPrototypeScene.unity" })
            {
                var preview = EditorSceneManager.OpenPreviewScene(scenePath);
                try
                {
                    var sceneDirector = preview.GetRootGameObjects()
                        .SelectMany(item => item.GetComponentsInChildren<PlayableDirector>(true))
                        .Single(item => item.playableAsset == sourceTimeline);
                    var sceneTarget = sceneDirector.GetGenericBinding(sourceTrack) as PlayerSkillImpactFrame;
                    Check(sceneTarget != null && sceneTarget.IsConfigured && sceneTarget.Effect != null &&
                        sceneTarget.Player == sceneDirector.GetComponentInParent<PlayerCore>().transform,
                        scenePath + " inherits the configured skill binding");
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }

            var realOwner = new object();
            bool realOriginal = sourceFeature.isActive;
            target.Configure(sourceRenderer, sourceFeature);
            target.SetRequestedActive(realOwner, true);
            Check(sourceFeature.isActive, "Actual PC_Renderer feature responds to the adapter");
            target.SetRequestedActive(realOwner, false);
            Check(!sourceFeature.isActive, "Actual feature switches OFF outside the interval");
            target.ReleaseOwner(realOwner);
            Check(sourceFeature.isActive == realOriginal, "Actual feature's baseline is restored");
            Check(File.ReadAllText(PlayerSkillImpactFrameSetup.RendererPath) == rendererBefore,
                "Renderer asset file is unchanged by preview/evaluation");
            Check(File.ReadAllText(materialPath) == materialBefore, "Impact Frame material file is unchanged");

            const string result = "PASS: start/end, scrub, overlap, multiple tracks/adapters, pause/stop, replay, hit stop, disable, baseline ON/OFF, rebinding, graph rebuild, mute, save/reload restoration, 30/60/120 fps samples, both scene bindings, real feature, unchanged renderer/material files.";
            File.WriteAllText(ResultPath, result);
            Debug.Log("TimelineActivationVerification: " + result);
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, "FAIL: " + exception);
            throw;
        }
        finally
        {
            TimelineActivationRegistry.RestoreAll();
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (TrackAsset track in timeline.GetRootTracks().ToArray())
                timeline.DeleteTrack(track);
            Object.DestroyImmediate(timeline);
            if (renderer != null) Object.DestroyImmediate(renderer);
            if (feature != null) Object.DestroyImmediate(feature);
            if (otherFeature != null) Object.DestroyImmediate(otherFeature);
        }
    }

    private static void Evaluate(PlayableDirector director, double time)
    {
        director.time = time;
        director.Evaluate();
    }

    private static void Near(double actual, double expected, string message) =>
        Check(Math.Abs(actual - expected) < 0.00001d, message);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("TimelineActivationVerification: " + message);
    }
}
