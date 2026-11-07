using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

public sealed partial class PlayerSkillStudio
{
    [SerializeField] private bool _loop = true;
    [SerializeField] private bool _focusSelectedClip;
    [SerializeField] private float _previewSpeed = 1f;
    [SerializeField] private int _presentationTab;
    private double _lastTick;
    private TimelineClip _animationClip;
    private TrackAsset _animationTrack;
    private SceneView _previewView;
    private bool _previewHadView;

    private double PreviewStart => _focusSelectedClip && _animationClip != null ? Math.Clamp(_animationClip.start, 0, Duration) : 0;
    private double PreviewEnd => _focusSelectedClip && _animationClip != null ? Math.Clamp(_animationClip.end, PreviewStart, Duration) : Duration;

    internal static double TimelineToSourceTime(TimelineClip clip, double time)
        => PlayerSkillTimelineUtility.TimelineToSourceTime(clip, time);

    internal static double SourceToTimelineTime(TimelineClip clip, double time)
        => PlayerSkillTimelineUtility.SourceToTimelineTime(clip, time);

    private void ValidateSelection()
    {
        if (Timeline == null) return;
        var animations = Tracks.OfType<AnimationTrack>().Where(track => !IsCameraTrack(track))
            .SelectMany(track => track.GetClips().Select(clip => (track: (TrackAsset)track, clip))).OrderBy(item => item.clip.start).ToArray();
        if (_animationClip == null || !animations.Any(item => item.clip == _animationClip))
        {
            _animationTrack = animations.Length > 0 ? animations[0].track : null;
            _animationClip = animations.Length > 0 ? animations[0].clip : null;
        }
        if (_clip != null && !Tracks.Any(track => track.GetClips().Contains(_clip))) _clip = null;
        if (_marker != null && !Tracks.Any(track => track.GetMarkers().Any(marker => ReferenceEquals(marker, _marker)))) _marker = null;
        if (_marker == null && _clip == null)
        {
            _marker = Tracks.OfType<PlayerSkillHitTrack>().SelectMany(PlayerSkillTimelineUtility.OrderedHits).FirstOrDefault();
            _track = _marker?.parent ?? _animationTrack;
        }
    }

    private void TickPreview()
    {
        double now = EditorApplication.timeSinceStartup;
        double delta = Math.Clamp(now - _lastTick, 0, .1);
        _lastTick = now;
        if (_previewHadView && _previewView == null) { EndPreview(); Repaint(); }
        if (EditorApplication.isPlaying) { Repaint(); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || _director == null) return;
        if (Data != null)
        {
            if (_playing)
            {
                double next = _time + delta * _previewSpeed * Data.PlaybackSpeed;
                if (next >= Duration)
                {
                    if (_loop) next %= Duration;
                    else { next = Duration; _playing = false; }
                }
                SampleDataPreview(next, false);
            }
            return;
        }
        // An external Timeline selection owns its own preview. Do not steal it back.
        if (_ownsPreview && (TimelineEditor.inspectedDirector != _director || !AnimationMode.InAnimationMode()))
        {
            _ownsPreview = false; _playing = false;
            Repaint();
            return;
        }
        if (_playing)
        {
            double start = PreviewStart, end = PreviewEnd;
            double next = _time + delta * _previewSpeed;
            if (next >= end)
            {
                if (_loop && end > start) next = start + (next - start) % (end - start);
                else { next = end; _playing = false; }
            }
            SamplePreview(next, false);
        }
        else if (TimelineEditor.inspectedDirector == _director)
        {
            var timelineWindow = TimelineEditor.GetWindow();
            if (timelineWindow != null)
            {
                double next = timelineWindow.playbackControls.GetCurrentTime();
                if (Math.Abs(next - _time) > .000001) { _time = next; Repaint(); SceneView.RepaintAll(); }
            }
        }
    }

    private void SamplePreview(double time, bool snap)
    {
        if (Data != null) { SampleDataPreview(time, snap); return; }
        if (_director == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        _time = Math.Clamp(snap ? Snap(Finite(time, 0)) : Finite(time, 0), 0, Duration);
        bool keepStudioFocus = focusedWindow == this;
        var window = OpenTimeline();
        // The native Timeline AnimationMode records/restores the scene, bindings and camera.
        // The Studio advances that paused preview clock to provide focus ranges and observation speed.
        if (!_ownsPreview || !AnimationMode.InAnimationMode())
        { window.playbackControls.Play(); window.playbackControls.Pause(); _ownsPreview = true; }
        window.playbackControls.Pause();
        window.playbackControls.SetCurrentTime(_time);
        TimelineEditor.Refresh(RefreshReason.SceneNeedsUpdate);
        // Creating the native Timeline window must not cover the Studio during a marker drag.
        if (keepStudioFocus && focusedWindow != this) Focus();
        SceneView.RepaintAll(); Repaint();
    }

    private void StartPreview(bool focus = true)
    {
        if (Data != null) { StartDataPreview(focus); return; }
        if (_director == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        SamplePreview(_time, false);
        if (!focus) return;
        if (_previewView == null)
        {
            _previewView = CreateInstance<SceneView>();
            _previewView.Show();
            _previewView.titleContent = new GUIContent("E Skill · 3D Preview");
            _previewHadView = true;
            float pixels = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            float desktopWidth = Mathf.Max(1280f, Screen.currentResolution.width / pixels);
            float desktopHeight = Mathf.Max(790f, Screen.currentResolution.height / pixels);
            float monitorLeft = Mathf.Floor(Mathf.Max(0, position.x) / desktopWidth) * desktopWidth;
            float studioWidth = Mathf.Clamp(position.width, 620f, 720f);
            float height = Mathf.Clamp(position.height, 680f, desktopHeight - 110f);
            float previewWidth = Mathf.Min(980f, desktopWidth - studioWidth - 40f);
            float y = Mathf.Clamp(position.y, 50f, Mathf.Max(50f, desktopHeight - height - 40f));
            position = new Rect(monitorLeft + previewWidth + 24f, y, studioWidth, height);
            _previewView.position = new Rect(monitorLeft + 12f, y, previewWidth, height);
        }
        var player = _director.GetComponentInParent<PlayerCore>();
        var target = player != null ? player.transform : _director.transform;
        _previewView.Frame(new Bounds(target.position + Vector3.up, new Vector3(7, 4, 7)), false);
        _previewView.Repaint();
        Focus();
    }

    private bool IsHitActive(PlayerSkillHitMarker hit)
        => hit != null && hit.Enabled && hit.parent != null && !hit.parent.mutedInHierarchy
            && Math.Round(_time * FrameRate) == Math.Round(hit.time * FrameRate);
}
