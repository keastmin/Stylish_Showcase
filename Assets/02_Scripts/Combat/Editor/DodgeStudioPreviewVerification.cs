using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Checks both sampled bones and actual body-only GPU-rendered images.</summary>
public static class DodgeStudioPreviewVerification
{
    private static readonly HumanBodyBones[] Bones = {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
    private static PlayerDodgeStudio _window;
    private static PlayerDodgeDefinition _definition;
    private static PlayerCore _player;
    private static string _authored;
    private static Quaternion[] _scenePose, _firstPose;
    private static RuntimeAnimatorController _sceneController;
    private static Vector3 _scenePosition;
    private static double _started;
    private static float _movement, _minSource, _maxSource;
    private static int _checks, _updates;
    private static readonly List<string> _evidence = new List<string>();
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MeshRenderer[] _rigidRenderers;
    private static bool[] _rigidEnabled;
    private static SkinnedMeshRenderer[] _bodyRenderers, _sceneSkins;
    private static bool[] _sceneOffscreen, _sceneForceSkin;
    private static Vector3[] _bodyPositions;
    private static Quaternion[] _bodyRotations;
    private static bool _renderStateSaved, _wasInPlace, _wasTargetVisible;
    private static int _renderCase, _renderStep, _changedBodyPixels;
    private static string _captureFile, _firstImage, _runId;
    private static double _captureStarted;

    [MenuItem("Tools/Combat/Validate Dodge Studio Preview")]
    public static void Run()
    {
        EditorApplication.update -= ObservePlayback;
        EditorApplication.update -= ObserveBodyRender;
        RestoreRenderState();
        _checks = _updates = 0; _movement = 0; _minSource = float.MaxValue; _maxSource = 0;
        _evidence.Clear();
        try
        {
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
            Check(_player != null && _player.Animator != null, "Scene has a preview source");
            _definition = _player.DodgeDefinition;
            _authored = EditorJsonUtility.ToJson(_definition);
            _sceneController = _player.Animator.runtimeAnimatorController;
            _scenePosition = _player.transform.position;
            _scenePose = ReadPose(_player.Animator);
            _sceneSkins = _player.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _sceneOffscreen = _sceneSkins.Select(s => s.updateWhenOffscreen).ToArray();
            _sceneForceSkin = _sceneSkins.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            PlayerDodgeStudio.OpenDefinition(_definition);
            _window = EditorWindow.GetWindow<PlayerDodgeStudio>();
            _window.PreparePreview();
            Check(_window.PreviewAnimator != _player.Animator, "Preview animates a separate model");
            Check(_window.PreviewAnimator.isHuman, "Actual player Avatar is used");

            float actorSpeed = _player.Animator.speed;
            try
            {
                _player.Animator.speed = 0;
                typeof(PlayerDodgeStudio).GetMethod("ClearPreview", Flags).Invoke(_window, null);
                _window.PreparePreview();
                Check(Mathf.Abs(_window.PreviewAnimator.speed - 1) < .0001f, "A stopped scene actor does not stop the preview clock");
                Check(_player.Animator.speed == 0, "Preparing the preview never changes the scene actor's speed");
            }
            finally { _player.Animator.speed = actorSpeed; }

            for (int sample = 0; sample < 5; sample++)
            {
                bool back = sample == 1;
                int phase = sample < 2 ? 0 : sample - 1;
                typeof(PlayerDodgeStudio).GetField("_backwards", Flags).SetValue(_window, back);
                var motion = _definition.Motion((DodgePhase)phase, back);
                Check(motion.clip != null, "Bound motion exists: " + motion.stateName);
                _window.InspectPose(phase, motion.End * .05f);
                _window.PreparePreview();
                var first = ReadPose(_window.PreviewAnimator);
                _window.InspectPose(phase, motion.End * .7f);
                _window.PreparePreview();
                float angle = Difference(first, ReadPose(_window.PreviewAnimator));
                Check(angle > 3, "Skeleton actually changes: " + motion.stateName);
                Check(Mathf.Abs(_window.PreviewSourceTime - motion.End * .7f) < .0001f, "Scrubbing evaluates the selected source frame");
                _evidence.Add(motion.stateName + ": sampled bone change " + angle.ToString("0.0") + " degrees (body pixels checked separately)");
            }
            typeof(PlayerDodgeStudio).GetField("_backwards", Flags).SetValue(_window, false);
            typeof(PlayerDodgeStudio).GetField("_phase", Flags).SetValue(_window, 0);
            _window.PlayPreview(1); _window.PreparePreview();
            float withoutCounter = (float)typeof(PlayerDodgeStudio).GetField("_duration", Flags).GetValue(_window);
            _window.PlayPreview(2); _window.PreparePreview();
            var events = (List<DodgeSequenceEvent>)typeof(PlayerDodgeStudio).GetField("_events", Flags).GetValue(_window);
            Check(events.Any(e => e.name == "반격 시작"), "Counter preview automatically selects a valid input time");
            _evidence.Add("Natural preview duration: " + withoutCounter.ToString("0.00") + "s; connected preview: " +
                ((float)typeof(PlayerDodgeStudio).GetField("_duration", Flags).GetValue(_window)).ToString("0.00") + "s");

            _window.PlayPreview(0); _window.PreparePreview();
            _firstPose = ReadPose(_window.PreviewAnimator);
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += ObservePlayback;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void ObservePlayback()
    {
        try
        {
            if (_window == null || _window.PreviewAnimator == null) throw new InvalidOperationException("Preview model disappeared during playback");
            _updates++;
            _movement = Mathf.Max(_movement, Difference(_firstPose, ReadPose(_window.PreviewAnimator)));
            _minSource = Mathf.Min(_minSource, _window.PreviewSourceTime);
            _maxSource = Mathf.Max(_maxSource, _window.PreviewSourceTime);
            if (EditorApplication.timeSinceStartup - _started < 1.5 || _updates < 8) return;
            Check(_window.PreviewPlaying, "Preview remains in playback across repaint events");
            Check(_maxSource - _minSource > .08f, "Playback advances actual sampled clip time");
            Check(_movement > 10, "Playback visibly moves the skeleton across multiple frames");
            Check(_player.Animator.runtimeAnimatorController == _sceneController, "Scene controller is unchanged");
            Check(Vector3.Distance(_player.transform.position, _scenePosition) < .0001f, "Scene player position is unchanged");
            Check(Difference(_scenePose, ReadPose(_player.Animator)) < .01f, "Scene player pose is unchanged");
            Check(EditorJsonUtility.ToJson(_definition) == _authored, "Opening, playing and changing views does not edit tuning data");
            _evidence.Add("Live editor updates: " + _updates + "; source range " + _minSource.ToString("0.000") + ".." + _maxSource.ToString("0.000") +
                "s; maximum displayed bone movement " + _movement.ToString("0.0") + " degrees");
            BeginBodyRenderChecks();
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void BeginBodyRenderChecks()
    {
        EditorApplication.update -= ObservePlayback;
        _window.PausePreview();
        _wasInPlace = (bool)typeof(PlayerDodgeStudio).GetField("_inPlace", Flags).GetValue(_window);
        _wasTargetVisible = (bool)typeof(PlayerDodgeStudio).GetField("_showTarget", Flags).GetValue(_window);
        _rigidRenderers = _window.PreviewAnimator.GetComponentsInChildren<MeshRenderer>(true);
        _rigidEnabled = _rigidRenderers.Select(r => r.enabled).ToArray();
        _bodyRenderers = _window.PreviewAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _renderStateSaved = true;
        Check(_bodyRenderers.Length > 0, "Body skin renderer exists");
        // Exclude the scythe and every rigid attachment. Root motion, camera
        // motion or a moving weapon must not make a frozen body pass this test.
        foreach (var renderer in _rigidRenderers) renderer.enabled = false;
        typeof(PlayerDodgeStudio).GetField("_inPlace", Flags).SetValue(_window, true);
        typeof(PlayerDodgeStudio).GetField("_showTarget", Flags).SetValue(_window, false);
        _runId = Guid.NewGuid().ToString("N");
        _renderCase = _renderStep = 0;
        RequestBodyFrame();
        EditorApplication.update += ObserveBodyRender;
    }

    private static void RequestBodyFrame()
    {
        bool back = _renderCase == 1;
        int phase = _renderCase < 2 ? 0 : _renderCase - 1;
        typeof(PlayerDodgeStudio).GetField("_backwards", Flags).SetValue(_window, back);
        var motion = _definition.Motion((DodgePhase)phase, back);
        _window.InspectPose(phase, motion.End * (_renderStep == 1 ? .7f : .05f));
        _window.PreparePreview();
        if (_renderStep == 0)
        {
            _bodyPositions = _bodyRenderers.Select(r => r.transform.position).ToArray();
            _bodyRotations = _bodyRenderers.Select(r => r.transform.rotation).ToArray();
        }
        else if (_renderStep == 1)
        {
            bool fixedRoot = true;
            for (int i = 0; i < _bodyRenderers.Length; i++)
                fixedRoot &= Vector3.Distance(_bodyPositions[i], _bodyRenderers[i].transform.position) < .0001f &&
                    Quaternion.Angle(_bodyRotations[i], _bodyRenderers[i].transform.rotation) < .01f;
            Check(fixedRoot, "Rendered-body comparison holds mesh roots fixed: " + motion.stateName);
        }
        _captureFile = Path.GetFullPath("Temp/DodgeBodyRender-" + _runId + "-" + _renderCase + "-" + _renderStep + ".png");
        _captureStarted = EditorApplication.timeSinceStartup;
        _window.CapturePreview(_captureFile);
    }

    private static void ObserveBodyRender()
    {
        try
        {
            if (!File.Exists(_captureFile))
            {
                if (EditorApplication.timeSinceStartup - _captureStarted > 10) throw new InvalidOperationException("Body render capture timed out; keep the preview window visible.");
                return;
            }
            if (_renderStep == 0) _firstImage = _captureFile;
            if (_renderStep == 1)
            {
                double mean;
                _changedBodyPixels = CompareRenderedImages(_firstImage, _captureFile, out mean);
                Check(_changedBodyPixels > 150 && mean > .02, "Body surface actually deforms when bones change; scythe hidden, roots fixed. Case " + _renderCase);
                _evidence.Add("Body render case " + _renderCase + ": " + _changedBodyPixels + " changed body-only pixels; mean RGB difference " + mean.ToString("0.000"));
            }
            if (_renderStep == 2)
            {
                double repeatMean;
                int repeated = CompareRenderedImages(_firstImage, _captureFile, out repeatMean);
                Check(repeated <= Mathf.Max(32, _changedBodyPixels / 20), "Returning to the same source pose reproduces the same rendered body");
                _evidence.Add("Same-pose repeat case " + _renderCase + ": " + repeated + " changed pixels");
                _renderCase++;
                _renderStep = 0;
            }
            else _renderStep++;
            if (_renderCase < 5) { RequestBodyFrame(); return; }
            for (int i = 0; i < _sceneSkins.Length; i++)
                Check(_sceneSkins[i].updateWhenOffscreen == _sceneOffscreen[i] && _sceneSkins[i].forceMatrixRecalculationPerRender == _sceneForceSkin[i],
                    "Preview skin refresh flags never modify the scene actor");
            Check(EditorJsonUtility.ToJson(_definition) == _authored, "Rendered-body checks preserve authored settings");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static int CompareRenderedImages(string first, string second, out double mean)
    {
        var a = new Texture2D(2, 2);
        var b = new Texture2D(2, 2);
        try
        {
            ImageConversion.LoadImage(a, File.ReadAllBytes(first));
            ImageConversion.LoadImage(b, File.ReadAllBytes(second));
            if (a.width != b.width || a.height != b.height) throw new InvalidOperationException("Preview size changed during the body render test.");
            var av = a.GetPixels32(); var bv = b.GetPixels32();
            long total = 0; int changed = 0;
            for (int i = 0; i < av.Length; i++)
            {
                int diff = Math.Abs(av[i].r - bv[i].r) + Math.Abs(av[i].g - bv[i].g) + Math.Abs(av[i].b - bv[i].b);
                total += diff;
                if (diff > 12) changed++;
            }
            mean = (double)total / (av.Length * 3);
            return changed;
        }
        finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
    }

    private static void RestoreRenderState()
    {
        if (!_renderStateSaved) return;
        EditorApplication.update -= ObserveBodyRender;
        for (int i = 0; i < _rigidRenderers.Length; i++) if (_rigidRenderers[i] != null) _rigidRenderers[i].enabled = _rigidEnabled[i];
        if (_window != null)
        {
            typeof(PlayerDodgeStudio).GetField("_inPlace", Flags).SetValue(_window, _wasInPlace);
            typeof(PlayerDodgeStudio).GetField("_showTarget", Flags).SetValue(_window, _wasTargetVisible);
        }
        _renderStateSaved = false;
    }

    private static Quaternion[] ReadPose(Animator animator)
        => Bones.Select(b => animator.GetBoneTransform(b) != null ? animator.GetBoneTransform(b).localRotation : Quaternion.identity).ToArray();
    private static float Difference(Quaternion[] a, Quaternion[] b)
    {
        float result = 0;
        for (int i = 0; i < a.Length; i++) result = Mathf.Max(result, Quaternion.Angle(a[i], b[i]));
        return result;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Finish(Exception exception)
    {
        EditorApplication.update -= ObservePlayback;
        EditorApplication.update -= ObserveBodyRender;
        RestoreRenderState();
        if (_window != null && exception == null)
        {
            typeof(PlayerDodgeStudio).GetField("_backwards", Flags).SetValue(_window, false);
            typeof(PlayerDodgeStudio).GetField("_phase", Flags).SetValue(_window, 0);
            _window.PlayPreview(0);
        }
        string result = exception == null ? "PASS " + _checks + " preview checks" : "FAIL " + exception;
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/DodgeStudioPreview.result", result + "\n" + string.Join("\n", _evidence));
        if (exception == null) Debug.Log(result);
        else Debug.LogException(exception);
    }
}
