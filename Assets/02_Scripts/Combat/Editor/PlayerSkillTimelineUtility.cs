using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Edits the real E Timeline while keeping hit identity independent of time and row order.</summary>
public static class PlayerSkillTimelineUtility
{
    public static PlayerSkillHitMarker[] OrderedHits(TrackAsset track)
    {
        if (track == null) return Array.Empty<PlayerSkillHitMarker>();
        // LINQ's stable order preserves the original notification order at equal times.
        var chronological = track.GetMarkers().OfType<PlayerSkillHitMarker>().OrderBy(hit => hit.time).ToArray();
        return chronological.OrderBy(hit => hit.DisplayOrder < 0 ? int.MaxValue : hit.DisplayOrder).ToArray();
    }

    public static int StabilizeHitFields(TrackAsset track, string undoName = "Stabilize E skill hit fields")
    {
        var hits = OrderedHits(track);
        if (hits.Length == 0) return 0;
        // Resolve every legacy ID before editing any marker. In particular, marker names
        // are not identities: authored 'Hit 12' markers can currently resolve to Hit8/Hit9.
        var fields = hits.Select(hit => hit.DamageFieldNumber).ToArray();
        var changed = hits.Where((hit, index) => hit.ExplicitDamageFieldNumber == 0 || hit.DisplayOrder != index).ToArray();
        if (changed.Length == 0) return 0;
        Undo.RecordObjects(changed.Cast<Object>().ToArray(), undoName);
        for (int index = 0; index < hits.Length; index++)
        {
            var hit = hits[index];
            if (hit.ExplicitDamageFieldNumber == 0) hit.ExplicitDamageFieldNumber = fields[index];
            hit.DisplayOrder = index;
            EditorUtility.SetDirty(hit);
        }
        MarkTimeline(track);
        return changed.Length;
    }

    public static bool MoveHit(PlayerSkillHitMarker hit, int direction)
    {
        if (hit == null || hit.parent == null || direction == 0) return false;
        var hits = OrderedHits(hit.parent);
        int index = Array.IndexOf(hits, hit);
        int next = Mathf.Clamp(index + Math.Sign(direction), 0, hits.Length - 1);
        if (index < 0 || next == index) return false;
        const string undoName = "Reorder E skill hit rows";
        StabilizeHitFields(hit.parent, undoName);
        Undo.RecordObjects(new Object[] { hits[index], hits[next] }, undoName);
        hits[index].DisplayOrder = next;
        hits[next].DisplayOrder = index;
        EditorUtility.SetDirty(hits[index]);
        EditorUtility.SetDirty(hits[next]);
        MarkTimeline(hit.parent);
        Refresh();
        return true;
    }

    public static bool SetMarkerTime(IMarker marker, double time)
    {
        if (marker is not Object owner || double.IsNaN(time) || double.IsInfinity(time)) return false;
        time = Math.Max(0d, time);
        if (Math.Abs(marker.time - time) < 1e-10d) return false;
        const string undoName = "Move E skill marker";
        if (marker is PlayerSkillHitMarker) StabilizeHitFields(marker.parent, undoName);
        Undo.RecordObject(owner, undoName);
        marker.time = time;
        EditorUtility.SetDirty(owner);
        MarkTimeline(marker.parent);
        Refresh();
        return true;
    }

    public static int NextUnusedField(TrackAsset track)
    {
        var used = OrderedHits(track).Select(hit => hit.DamageFieldNumber).ToArray();
        for (int field = 1; field <= PlayerSkillAttackReceiver.DamageFieldCount; field++)
            if (!used.Contains(field)) return field;
        return PlayerSkillAttackReceiver.DamageFieldCount;
    }

    public static double TimelineToSourceTime(TimelineClip clip, double time, bool clampToClip = true)
    {
        if (clip == null) return 0d;
        if (double.IsNaN(time) || double.IsInfinity(time)) time = clip.start;
        if (clampToClip) time = Math.Max(clip.start, Math.Min(clip.end, time));
        return clip.clipIn + (time - clip.start) * clip.timeScale;
    }

    public static double SourceToTimelineTime(TimelineClip clip, double sourceTime, bool clampToClip = true)
    {
        if (clip == null) return 0d;
        if (double.IsNaN(sourceTime) || double.IsInfinity(sourceTime)) sourceTime = clip.clipIn;
        double speed = Math.Max(.000001d, clip.timeScale);
        double time = clip.start + (sourceTime - clip.clipIn) / speed;
        return clampToClip ? Math.Max(clip.start, Math.Min(clip.end, time)) : time;
    }

    private static void MarkTimeline(TrackAsset track)
    {
        if (track == null) return;
        EditorUtility.SetDirty(track);
        if (track.timelineAsset != null) EditorUtility.SetDirty(track.timelineAsset);
    }

    private static void Refresh() => TimelineEditor.Refresh(RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate);
}
