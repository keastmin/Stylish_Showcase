using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Observes real scene AI without driving attacks or changing authored settings.</summary>
[InitializeOnLoad]
public static class EnemyGroupCombatPlayVerification
{
    private const string Pending = "ProjectR.GroupCombat.Observe";
    private const string Background = "ProjectR.GroupCombat.PreviousBackground";
    private const string RestoreBackground = "ProjectR.GroupCombat.RestoreBackground";
    private const string ReportPath = "output/EnemyGroupCombat/runtime.json";
    private static readonly Dictionary<EnemyCore, Entry> Entries = new();
    private static readonly Dictionary<EnemyAttackTimingController, EnemyCore> Previous = new();
    private static readonly Dictionary<EnemyAttackTimingController, float> EndTimes = new();
    private static Report _report;
    private static int _lastFrame;
    private static double _deadline;
    private static bool _background;

    [Serializable] public sealed class Entry
    {
        public string enemy;
        public int starts;
        public float engagedSeconds;
        public float maxDistanceToTarget;
    }

    [Serializable] public sealed class Report
    {
        public string status;
        public string detail;
        public float observedCombatSeconds;
        public int observedFrames;
        public int attackStarts;
        public int maxConcurrentAttacksPerGroup;
        public int uncoordinatedFrames;
        public int ownerMismatchFrames;
        public float minimumHandoffGap = -1f;
        public float minimumNeighbourDistance = -1f;
        public Entry[] enemies;
    }

    static EnemyGroupCombatPlayVerification()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RestoreBackground, false)) return;
            SessionState.SetBool(RestoreBackground, false);
            Application.runInBackground = SessionState.GetBool(Background, false);
            if (!SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory("output/EnemyGroupCombat");
            File.WriteAllText(ReportPath, "{\"status\":\"INCOMPLETE\",\"detail\":\"Play mode ended before observation completed.\"}");
            _report = null;
        };
    }

    [MenuItem("Tools/Character/Enemy/Observe Group Combat in Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start group observation in Edit mode.");
        Directory.CreateDirectory("output/EnemyGroupCombat");
        File.WriteAllText(ReportPath, "{\"status\":\"starting\"}");
        SessionState.SetBool(Background, Application.runInBackground);
        SessionState.SetBool(RestoreBackground, true);
        Application.runInBackground = true;
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || EditorApplication.isCompiling ||
            !EditorApplication.isPlaying) return;
        try
        {
            if (_report == null)
            {
                Entries.Clear(); Previous.Clear(); EndTimes.Clear();
                _report = new Report { status = "running" };
                _background = SessionState.GetBool(Background, false);
                Application.runInBackground = true;
                _deadline = EditorApplication.timeSinceStartup + 180;
            }
            if (Time.frameCount < 5 || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                Finish("INCOMPLETE", "Observation exceeded the wall clock deadline.");
                return;
            }
            if (VirtualCutscenePlayback.IsPlaying || ProjectR.UI.PauseSettingsController.InputBlocked) return;
            float dt = CombatTimeController.DeltaTime;
            _report.observedCombatSeconds += dt;
            _report.observedFrames++;
            var enemies = UnityEngine.Object.FindObjectsByType<EnemyCore>();
            var active = new Dictionary<EnemyAttackTimingController, int>();
            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;
                if (!Entries.TryGetValue(enemy, out Entry entry))
                    Entries.Add(enemy, entry = new Entry { enemy = enemy.name + "#" + enemy.GetEntityId().GetHashCode() });
                if (enemy.IsEngagedForCoordination) entry.engagedSeconds += dt;
                if (enemy.TargetTransform != null)
                    entry.maxDistanceToTarget = Mathf.Max(entry.maxDistanceToTarget,
                        Vector3.Distance(enemy.transform.position, enemy.TargetTransform.position));
                if (enemy.AttackTimingController == null || enemy.PositioningController == null)
                    _report.uncoordinatedFrames++;
                if (!enemy.IsInAttackSequence) continue;
                var group = enemy.AttackTimingController;
                if (group == null) continue;
                active.TryGetValue(group, out int count);
                active[group] = count + 1;
                if (group.ActiveAttacker != enemy) _report.ownerMismatchFrames++;
            }
            var groups = UnityEngine.Object.FindObjectsByType<EnemyAttackTimingController>();
            foreach (var group in groups)
            {
                active.TryGetValue(group, out int count);
                _report.maxConcurrentAttacksPerGroup = Mathf.Max(_report.maxConcurrentAttacksPerGroup, count);
                Previous.TryGetValue(group, out EnemyCore prior);
                EnemyCore current = group.ActiveAttacker;
                if (prior == current) continue;
                if (prior != null) EndTimes[group] = _report.observedCombatSeconds;
                if (current != null)
                {
                    _report.attackStarts++;
                    if (Entries.TryGetValue(current, out Entry entry)) entry.starts++;
                    if (EndTimes.TryGetValue(group, out float end))
                    {
                        float gap = _report.observedCombatSeconds - end;
                        _report.minimumHandoffGap = _report.minimumHandoffGap < 0 ? gap : Mathf.Min(_report.minimumHandoffGap, gap);
                    }
                }
                Previous[group] = current;
            }
            for (int i = 0; i < enemies.Length; i++)
            for (int j = i + 1; j < enemies.Length; j++)
            {
                if (!enemies[i].IsEngagedForCoordination || !enemies[j].IsEngagedForCoordination) continue;
                Vector3 offset = enemies[i].transform.position - enemies[j].transform.position;
                offset.y = 0;
                float distance = offset.magnitude;
                _report.minimumNeighbourDistance = _report.minimumNeighbourDistance < 0 ? distance : Mathf.Min(_report.minimumNeighbourDistance, distance);
            }
            if (_report.observedCombatSeconds < 45f) return;
            bool everyEngagedEnemyAttacked = true;
            foreach (Entry entry in Entries.Values)
                if (entry.engagedSeconds > 10f && entry.starts == 0) everyEngagedEnemyAttacked = false;
            bool passed = Entries.Count >= 3 && _report.attackStarts >= 5 &&
                _report.maxConcurrentAttacksPerGroup <= 1 && _report.uncoordinatedFrames == 0 &&
                _report.ownerMismatchFrames == 0 && everyEngagedEnemyAttacked;
            Finish(passed ? "PASS" : "FAIL", "Live scene observation; timing/ownership assertions only. Spacing is measured, not a subjective gameplay-quality verdict.");
        }
        catch (Exception ex) { Finish("FAIL", ex.ToString()); }
    }

    private static void Finish(string status, string detail)
    {
        _report ??= new Report();
        _report.status = status; _report.detail = detail;
        _report.enemies = new List<Entry>(Entries.Values).ToArray();
        Directory.CreateDirectory("output/EnemyGroupCombat");
        File.WriteAllText(ReportPath, JsonUtility.ToJson(_report, true));
        SessionState.SetBool(Pending, false);
        Application.runInBackground = _background;
        Debug.Log("Group combat observation: " + status + " (" + _report.attackStarts + " starts). " + ReportPath);
        _report = null;
        EditorApplication.ExitPlaymode();
    }
}
