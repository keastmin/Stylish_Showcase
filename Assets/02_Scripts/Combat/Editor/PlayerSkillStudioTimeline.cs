using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

public sealed partial class PlayerSkillStudio
{
    private Vector2 _hitTimelineScroll;
    private PlayerSkillHitMarker _dragMarker;
    private int _dragUndo = -1, _dragControl;
    private float _dragX;
    private double _dragStart, _dragDuration;
    private bool _showOverview;

    private void DrawSequenceStrip()
    {
        var entries = Tracks.OfType<AnimationTrack>().Where(track => !IsCameraTrack(track))
            .SelectMany(track => track.GetClips().Select(clip => (track: (TrackAsset)track, clip))).OrderBy(item => item.clip.start).ToArray();
        if (entries.Length == 0) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            int selected = Array.FindIndex(entries, item => item.clip == _animationClip);
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("편집할 클립", Math.Max(0, selected), entries.Select((item, index) => $"{index + 1:00} · {item.clip.displayName}").ToArray());
            if (EditorGUI.EndChangeCheck()) SelectAnimationClip(entries[next].track, entries[next].clip);
            if (GUILayout.Button("클립 구성", GUILayout.Width(75)))
            {
                _tab = 1; _clip = _animationClip; _track = _animationTrack; _marker = null;
            }
        }
        Rect strip = GUILayoutUtility.GetRect(0, 39, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(strip, SkillStudioGUI.StripBackground);
        for (int i = 0; i < entries.Length; i++)
        {
            var item = entries[i];
            Rect block = new(strip.x + (float)(item.clip.start / Duration) * strip.width, strip.y + 2,
                Mathf.Max(1, (float)(item.clip.duration / Duration) * strip.width - 2), strip.height - 4);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = item.track.mutedInHierarchy ? Color.gray : item.clip == _animationClip ? Accent : new Color(.6f, .62f, .72f);
            string title = block.width < 95 ? $"{i + 1:00}\n{item.clip.start:0.00}s" : $"{i + 1:00} · {item.clip.displayName}\n{item.clip.start:0.00}–{item.clip.end:0.00}s";
            if (GUI.Button(block, new GUIContent(title, $"{item.track.name} / {item.clip.displayName} · 클릭해서 편집 대상으로 선택"))) SelectAnimationClip(item.track, item.clip);
            GUI.backgroundColor = old;
        }
        EditorGUI.DrawRect(new Rect(strip.x + Mathf.Clamp01((float)(_time / Duration)) * strip.width, strip.y, 2, strip.height), HitColor);
    }

    private void SelectAnimationClip(TrackAsset track, TimelineClip clip)
    {
        _animationTrack = track; _animationClip = clip;
        _track = track; _clip = clip; _marker = null; _tab = 1;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Seek(clip.start);
        Repaint();
    }

    private void DrawHitTimeline()
    {
        var hits = Tracks.OfType<PlayerSkillHitTrack>().SelectMany(PlayerSkillTimelineUtility.OrderedHits).ToArray();
        float height = 28 + 24 * Math.Max(1, hits.Length);
        float visibleHeight = Mathf.Min(height, Mathf.Clamp(position.height * .27f, 124f, 244f));
        _hitTimelineScroll = EditorGUILayout.BeginScrollView(_hitTimelineScroll, GUILayout.Height(visibleHeight));
        Rect area = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        float left = area.x + SkillStudioGUI.TimelineLabelWidth, width = Mathf.Max(1, area.width - SkillStudioGUI.TimelineLabelWidth - 14);
        EditorGUI.DrawRect(area, SkillStudioGUI.TimelineBackground);
        for (int i = 0; i <= 10; i++)
        {
            float x = left + width * i / 10f;
            EditorGUI.DrawRect(new Rect(x, area.y + 19, 1, area.height - 19), SkillStudioGUI.TimelineGrid);
            GUI.Label(new Rect(x - 12, area.y, 44, 18), (Duration * i / 10d).ToString("0.0"), EditorStyles.miniLabel);
        }
        GUI.Label(new Rect(area.x + 5, area.y, 155, 18), "타격 마커 · 전체 시퀀스 초", EditorStyles.miniLabel);
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event ev = Event.current;
        bool editable = !EditorApplication.isPlayingOrWillChangePlaymode;
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            bool selected = ReferenceEquals(_marker, hit);
            float y = area.y + 23 + i * 24;
            if (GUI.Button(new Rect(area.x + 5, y, 105, 21), new GUIContent($"{i + 1:00}  {MarkerName(hit)}", hit.name), selected ? EditorStyles.toolbarButton : EditorStyles.miniLabel)) SelectMarker(hit.parent, hit);
            var siblings = PlayerSkillTimelineUtility.OrderedHits(hit.parent);
            int index = Array.IndexOf(siblings, hit);
            using (new EditorGUI.DisabledScope(!editable || index == 0 || _dragMarker != null))
                if (GUI.Button(new Rect(area.x + 112, y, 21, 21), new GUIContent("↑", "타격 행을 위로 이동 · 시각과 판정은 유지"), EditorStyles.miniButtonLeft)) MoveHitRow(hit, -1);
            using (new EditorGUI.DisabledScope(!editable || index >= siblings.Length - 1 || _dragMarker != null))
                if (GUI.Button(new Rect(area.x + 134, y, 21, 21), new GUIContent("↓", "타격 행을 아래로 이동 · 시각과 판정은 유지"), EditorStyles.miniButtonRight)) MoveHitRow(hit, 1);
            float markerX = left + Mathf.Clamp01((float)(hit.time / Duration)) * width;
            Rect markerRect = new(markerX - 9, y, 18, 22);
            EditorGUI.DrawRect(new Rect(left, y + 11, width, 1), SkillStudioGUI.TimelineGrid);
            Color color = !hit.Enabled || hit.parent.mutedInHierarchy ? Color.gray : IsHitActive(hit) ? Color.white : selected ? HitColor : Accent;
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = color } };
            int frame = (int)Math.Round(hit.time * FrameRate);
            GUI.Label(markerRect, new GUIContent("◆", $"{MarkerName(hit)} · {frame}f · 전체 {hit.time:0.000}초 · 드래그해서 이동"), style);
            GUI.Label(new Rect(markerX + 12 > area.xMax - 42 ? markerX - 46 : markerX + 12, y + 3, 42, 18), $"{frame}f", EditorStyles.miniLabel);
            if (editable) EditorGUIUtility.AddCursorRect(markerRect, MouseCursor.SlideArrow);
            if (editable && ev.type == EventType.MouseDown && ev.button == 0 && markerRect.Contains(ev.mousePosition))
            {
                SelectMarker(hit.parent, hit);
                _dragMarker = hit; _dragStart = hit.time; _dragDuration = Duration; _dragX = ev.mousePosition.x;
                Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Move E skill hit marker");
                _dragControl = control; GUIUtility.hotControl = control; ev.Use();
            }
        }
        if (hits.Length == 0) GUI.Label(new Rect(area.x + 5, area.y + 25, area.width - 10, 20), "타격이 없습니다. 아래에서 현재 시각에 타격을 추가하세요.", EditorStyles.miniLabel);
        if (_dragMarker != null && GUIUtility.hotControl == _dragControl)
        {
            if (ev.type == EventType.MouseDrag)
            {
                double next = Math.Clamp(Snap(_dragStart + (ev.mousePosition.x - _dragX) / width * _dragDuration), 0, _dragDuration);
                PlayerSkillTimelineUtility.SetMarkerTime(_dragMarker, next);
                Mark(_dragMarker); Seek(next); ev.Use();
            }
            else if (ev.type == EventType.MouseUp) { EndMarkerDrag(); ev.Use(); }
        }
        // Mouse-up can be consumed by a different GUI control or arrive outside the lane.
        if (_dragMarker != null && (ev.rawType == EventType.MouseUp || ev.type == EventType.Ignore || GUIUtility.hotControl != _dragControl))
            EndMarkerDrag();
        if (editable && ev.type == EventType.MouseDown && ev.button == 0 && new Rect(left, area.y, width, 20).Contains(ev.mousePosition))
        {
            Seek((ev.mousePosition.x - left) / width * Duration); ev.Use();
        }
        EditorGUI.DrawRect(new Rect(left + Mathf.Clamp01((float)(_time / Duration)) * width, area.y + 19, 2, area.height - 19), HitColor);
        EditorGUILayout.EndScrollView();
    }

    private void MoveHitRow(PlayerSkillHitMarker hit, int direction)
    {
        PlayerSkillTimelineUtility.MoveHit(hit, direction);
        Mark(hit); Mark(hit.parent); Repaint();
    }

    private void EndMarkerDrag()
    {
        EndDataMarkerDrag();
        if (_dragUndo >= 0) Undo.CollapseUndoOperations(_dragUndo);
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragMarker = null; _dragUndo = -1; _dragControl = 0;
    }
}
