using System;
using System.Collections.Generic;
using ProjectR.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Owns the render surface and a reversible suspension of the combat world.</summary>
[DefaultExecutionOrder(-9500)]
public sealed class VirtualCutscenePlayback : MonoBehaviour
{
    private static VirtualCutscenePlayback _active;
    private static double _suspendedSeconds, _suspendStarted;
    private static int _resumeAfterFrame = -1;
    public static bool IsPlaying => _active != null && !_active._finished;
    public static bool InputBlocked => IsPlaying || Time.frameCount <= _resumeAfterFrame;
    // Hitstop deadlines use this clock so they retain their remaining duration across a cutscene.
    public static double CombatUnscaledTime => PauseSettingsController.UnpausedTime - _suspendedSeconds -
        (IsPlaying ? PauseSettingsController.UnpausedTime - _suspendStarted : 0);
    public static VirtualCutscenePlayback Active => _active;
    public float Elapsed { get; private set; }
    public VirtualCutsceneStage Stage => _stage;
    public RenderTexture Output => _texture;
    public VirtualCutsceneDefinition Definition => _definition;

    private struct CameraState
    {
        public Camera camera;
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
    }
    private readonly List<CameraState> _cameras = new();
    private readonly List<Light> _lights = new();
    private readonly List<Behaviour> _cameraControllers = new();
    private readonly List<(Animator animator, float speed)> _animators = new();
    private readonly List<AudioSource> _audio = new();
    private bool[] _soundStarted;
    private PlayerCore _owner;
    private VirtualCutsceneDefinition _definition;
    private VirtualCutsceneStage _stage;
    private Scene _scene;
    private Camera _camera;
    private Camera _displayCamera;
    private RenderTexture _texture;
    private GameObject _overlay;
    private RawImage _image;
    private Image _black;
    private Action<bool> _onFinished;
    private float _previousTimeScale;
    private bool _previousAudioPause, _suspended, _finished;
    private int _width, _height;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _active = null;
        _suspendedSeconds = 0;
        _suspendStarted = 0;
        _resumeAfterFrame = -1;
    }

    public static bool TryPlay(VirtualCutsceneDefinition definition, PlayerCore owner, Action<bool> onFinished)
    {
        if (!Application.isPlaying || IsPlaying || definition == null || !definition.Ready || owner == null || !owner.isActiveAndEnabled || Time.timeScale <= 0) return false;
        var go = new GameObject("Virtual Cutscene Playback") { hideFlags = HideFlags.DontSave };
        var player = go.AddComponent<VirtualCutscenePlayback>();
        player._owner = owner;
        try
        {
            player.Initialize(definition);
            player._onFinished = onFinished;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, owner);
            player.Finish(false);
            return false;
        }
    }

    public static void CancelFor(PlayerCore owner)
    {
        if (IsPlaying && _active._owner == owner) _active.Finish(false);
    }

    private void Initialize(VirtualCutsceneDefinition definition)
    {
        _definition = Instantiate(definition);
        _definition.hideFlags = HideFlags.HideAndDontSave;
        _previousTimeScale = Time.timeScale;
        _previousAudioPause = AudioListener.pause;
        // Acquire ownership before the first mutation, so a partial setup failure
        // follows exactly the same restoration path as a completed cutscene.
        _suspendStarted = PauseSettingsController.UnpausedTime;
        _active = this;
        _suspended = true;
        Time.timeScale = 0;
        AudioListener.pause = true;
        foreach (var camera in Object.FindObjectsByType<Camera>())
            if (camera.enabled && camera.gameObject.activeInHierarchy)
            {
                _cameras.Add(new CameraState { camera = camera, position = camera.transform.position,
                    rotation = camera.transform.rotation, fieldOfView = camera.fieldOfView });
                camera.enabled = false;
            }
        foreach (var controller in Object.FindObjectsByType<CinemachineBrain>()) SuspendController(controller);
        foreach (var controller in Object.FindObjectsByType<CinemachineInputAxisController>()) SuspendController(controller);
        foreach (var light in Object.FindObjectsByType<Light>())
            if (light.enabled) { _lights.Add(light); light.enabled = false; }
        foreach (var animator in Object.FindObjectsByType<Animator>())
            if (animator.enabled) { _animators.Add((animator, animator.speed)); animator.speed = 0; }
        _scene = SceneManager.CreateScene("Virtual Cutscene Stage " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var cameraObject = new GameObject("Cutscene Render Camera") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(cameraObject, _scene);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.depth = 100;
        _stage = new VirtualCutsceneStage(_definition, _owner.Animator, _camera);
        SceneManager.MoveGameObjectToScene(_stage.Root, _scene);
        // A separate physics scene and render layer keep authored props away from combat.
        _stage.Root.transform.position = new Vector3(10000, 10000, 10000);
        _stage.Sample(0);
        bool hasListener = false;
        foreach (var listener in Object.FindObjectsByType<AudioListener>()) if (listener.isActiveAndEnabled) { hasListener = true; break; }
        if (!hasListener) cameraObject.AddComponent<AudioListener>();
        CreateOverlay();
        ResizeSurface();
        _soundStarted = new bool[_definition.sounds?.Count ?? 0];
        for (int i = 0; i < _soundStarted.Length; i++)
        {
            var source = cameraObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.ignoreListenerPause = true;
            source.dopplerLevel = 0;
            _audio.Add(source);
        }
        _black.color = new Color(0, 0, 0, _definition.BlackOpacity(0));
    }

    private void SuspendController(Behaviour controller)
    {
        if (controller.enabled) { _cameraControllers.Add(controller); controller.enabled = false; }
    }

    private void CreateOverlay()
    {
        _overlay = new GameObject("Cutscene Screen", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(_overlay, _scene);
        var canvas = _overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        _image = MakeUI<RawImage>("Render Surface");
        _image.raycastTarget = false;
        for (int i = 0; i < 2; i++)
        {
            var bar = MakeUI<Image>(i == 0 ? "Letterbox Bottom" : "Letterbox Top");
            bar.color = Color.black;
            bar.raycastTarget = false;
            bar.rectTransform.anchorMin = new Vector2(0, i == 0 ? 0 : 1 - _definition.letterbox);
            bar.rectTransform.anchorMax = new Vector2(1, i == 0 ? _definition.letterbox : 1);
        }
        _black = MakeUI<Image>("Cutscene Fade");
        _black.raycastTarget = false;
        CreateDisplayCamera(canvas.targetDisplay);
    }

    private void CreateDisplayCamera(int targetDisplay)
    {
        // The stage camera only renders into a texture. Keep a camera on the
        // overlay's display so Game View does not show "No cameras rendering".
        var cameraObject = new GameObject("Cutscene Display Camera") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(cameraObject, _scene);
        _displayCamera = cameraObject.AddComponent<Camera>();
        _displayCamera.targetDisplay = targetDisplay;
        _displayCamera.depth = _camera.depth + 1;
        _displayCamera.cullingMask = 0;
        _displayCamera.clearFlags = CameraClearFlags.SolidColor;
        _displayCamera.backgroundColor = Color.black;
        _displayCamera.allowHDR = false;
        _displayCamera.allowMSAA = false;
        _displayCamera.useOcclusionCulling = false;
        var data = _displayCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;
        data.volumeLayerMask = 0;
    }

    private T MakeUI<T>(string label) where T : Graphic
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(_overlay.transform, false);
        var graphic = go.AddComponent<T>();
        graphic.rectTransform.anchorMin = Vector2.zero;
        graphic.rectTransform.anchorMax = Vector2.one;
        graphic.rectTransform.offsetMin = graphic.rectTransform.offsetMax = Vector2.zero;
        return graphic;
    }

    private void ResizeSurface()
    {
        int width = Mathf.Max(16, Screen.width), height = Mathf.Max(16, Screen.height);
        if (_texture != null && width == _width && height == _height) return;
        _width = width; _height = height;
        if (_texture != null) { _camera.targetTexture = null; _texture.Release(); Destroy(_texture); }
        float scale = Mathf.Min(1, 1920f / width, 1080f / height);
        _texture = new RenderTexture(Mathf.Max(16, Mathf.RoundToInt(width * scale)), Mathf.Max(16, Mathf.RoundToInt(height * scale)), 24)
            { name = "Virtual Cutscene Render Surface", hideFlags = HideFlags.DontSave };
        _texture.Create();
        _camera.targetTexture = _texture;
        _camera.aspect = width / (float)height;
        _image.texture = _texture;
    }

    private void Update()
    {
        if (_finished) return;
        if (_owner == null || !_owner.isActiveAndEnabled || !_owner.gameObject.scene.isLoaded) { Finish(false); return; }
        if (PauseSettingsController.InputBlocked) return;
        if (_definition.allowSkip && Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Finish(true); return; }
        Advance(Time.unscaledDeltaTime);
    }

    public void Advance(float realSeconds)
    {
        if (_finished || _definition == null || PauseSettingsController.IsPaused) return;
        if (!_stage.TryActivate()) return;
        Elapsed = Mathf.Min(_definition.Duration, Elapsed + Mathf.Max(0, realSeconds));
        if (Elapsed >= _definition.Duration) { Finish(true); return; }
        ResizeSurface();
        _stage.Sample(Elapsed);
        UpdateAudio();
        _black.color = new Color(0, 0, 0, _definition.BlackOpacity(Elapsed));
    }

    private void UpdateAudio()
    {
        for (int i = 0; i < _audio.Count; i++)
        {
            var entry = _definition.sounds[i];
            var source = _audio[i];
            if (entry?.clip == null) continue;
            bool active = Elapsed >= entry.start && Elapsed < entry.start + Mathf.Max(.01f, entry.duration);
            if (!active) { if (source.isPlaying) source.Stop(); continue; }
            source.volume = entry.VolumeAt(Elapsed);
            if (_soundStarted[i]) continue;
            _soundStarted[i] = true;
            float clipTime = entry.sourceStart + (Elapsed - entry.start) * entry.pitch;
            if (entry.loop) clipTime = Mathf.Repeat(clipTime, Mathf.Max(.001f, entry.clip.length));
            else if (clipTime >= entry.clip.length) continue;
            source.clip = entry.clip;
            source.pitch = entry.pitch;
            source.loop = entry.loop;
            source.time = Mathf.Clamp(clipTime, 0, Mathf.Max(0, entry.clip.length - .001f));
            source.Play();
        }
    }

    public void Finish(bool completed)
    {
        if (_finished) return;
        // Mark and detach first: a completion callback can stop its owning ultimate.
        if (_suspended) _suspendedSeconds += Math.Max(0, PauseSettingsController.UnpausedTime - _suspendStarted);
        _finished = true;
        if (_active == this) _active = null;
        _resumeAfterFrame = Time.frameCount + 1;
        foreach (var source in _audio) if (source != null) source.Stop();
        if (_camera != null) { _camera.enabled = false; _camera.targetTexture = null; }
        if (_displayCamera != null) _displayCamera.enabled = false;
        if (_overlay != null) _overlay.SetActive(false);
        if (_suspended)
        {
            PauseSettingsController.RestoreWorldTime(_previousTimeScale, _previousAudioPause);
            foreach (var entry in _animators) if (entry.animator != null) entry.animator.speed = entry.speed;
            foreach (var light in _lights) if (light != null) light.enabled = true;
            foreach (var entry in _cameras)
                if (entry.camera != null)
                {
                    entry.camera.transform.SetPositionAndRotation(entry.position, entry.rotation);
                    entry.camera.fieldOfView = entry.fieldOfView;
                    entry.camera.enabled = true;
                }
            foreach (var controller in _cameraControllers) if (controller != null) controller.enabled = true;
            PauseSettingsController.ReapplyCameraPause();
            _suspended = false;
        }
        _stage?.Dispose(); _stage = null;
        // Release the owned objects explicitly as well as unloading their scene;
        // DontSave objects can otherwise outlive an interrupted scene unload.
        if (_camera != null) { VirtualCutsceneStage.Remove(_camera.gameObject); _camera = null; }
        if (_displayCamera != null) { VirtualCutsceneStage.Remove(_displayCamera.gameObject); _displayCamera = null; }
        if (_overlay != null) { VirtualCutsceneStage.Remove(_overlay); _overlay = null; }
        if (_texture != null) { _texture.Release(); Destroy(_texture); _texture = null; }
        if (_scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
        if (_definition != null) Destroy(_definition);
        _definition = null;
        var callback = _onFinished; _onFinished = null;
        try { callback?.Invoke(completed); }
        finally { if (this != null) Destroy(gameObject); }
    }

    private void OnDisable() => Finish(false);
    private void OnDestroy() => Finish(false);
}
