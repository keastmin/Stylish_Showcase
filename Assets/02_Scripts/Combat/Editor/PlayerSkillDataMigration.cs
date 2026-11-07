using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>One-time, non-destructive import. Reconnecting never overwrites authored E data.</summary>
public static class PlayerSkillDataMigration
{
    public const string DefinitionPath = "Assets/07_Datas/Player/PlayerSkill.asset";

    [MenuItem("Tools/Character/Player/Connect E Skill Data")]
    public static void Connect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("E 데이터 연결은 편집 모드에서 실행하세요.");
        // Capture scene overrides before updating the prefab: inherited references
        // become non-null immediately when its definition is connected.
        var imports = new Dictionary<PlayerSkillAttackReceiver, PlayerSkillDefinition>();
        foreach (var receiver in Object.FindObjectsByType<PlayerSkillAttackReceiver>(FindObjectsInactive.Include))
        {
            if (!receiver.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(receiver.gameObject.scene)) continue;
            if (receiver.GetComponent<PlayerSkillSequenceSettings>()?.Definition != null) continue;
            try { imports.Add(receiver, Import(receiver)); }
            catch { foreach (var data in imports.Values) Object.DestroyImmediate(data); throw; }
        }
        var prefab = PrefabUtility.LoadPrefabContents(PlayerSkillStudioSetup.PlayerPath);
        PlayerSkillDefinition definition = null;
        try
        {
            var receiver = prefab.GetComponentInChildren<PlayerSkillAttackReceiver>(true);
            if (receiver == null) throw new InvalidOperationException("Player prefab의 E 타격 연결이 없습니다.");
            var settings = receiver.GetComponent<PlayerSkillSequenceSettings>();
            if (settings == null) settings = receiver.gameObject.AddComponent<PlayerSkillSequenceSettings>();
            definition = settings.Definition;
            if (definition == null) definition = AssetDatabase.LoadAssetAtPath<PlayerSkillDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = Import(receiver);
                AssetDatabase.CreateAsset(definition, DefinitionPath);
                AssetDatabase.SaveAssetIfDirty(definition);
            }
            if (settings.Definition != definition)
            {
                settings.Definition = definition;
                PrefabUtility.SaveAsPrefabAsset(prefab, PlayerSkillStudioSetup.PlayerPath);
            }
        }
        catch
        {
            foreach (var data in imports.Values) Object.DestroyImmediate(data);
            throw;
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (var receiver in Object.FindObjectsByType<PlayerSkillAttackReceiver>(FindObjectsInactive.Include))
        {
            if (!receiver.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(receiver.gameObject.scene)) continue;
            var settings = receiver.GetComponent<PlayerSkillSequenceSettings>();
            if (settings == null) settings = Undo.AddComponent<PlayerSkillSequenceSettings>(receiver.gameObject);
            PlayerSkillDefinition assigned = definition;
            if (imports.TryGetValue(receiver, out var imported) && !Equivalent(imported, definition))
            {
                string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/07_Datas/Player/PlayerSkill_" + receiver.gameObject.scene.name + ".asset");
                assigned = imported;
                AssetDatabase.CreateAsset(assigned, scenePath);
                AssetDatabase.SaveAssetIfDirty(assigned);
            }
            if (settings.Definition == assigned || settings.Definition != null && !imports.ContainsKey(receiver)) continue;
            Undo.RecordObject(settings, "Connect E Skill Data");
            settings.Definition = assigned;
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            EditorSceneManager.MarkSceneDirty(receiver.gameObject.scene);
        }
        foreach (var imported in imports.Values) if (!AssetDatabase.Contains(imported)) Object.DestroyImmediate(imported);
        Debug.Log($"E 데이터 연결 완료: {AssetDatabase.GetAssetPath(definition)} · {definition.hits.Count}타격 · {definition.motion.distance:0.###}m. Timeline은 애니메이션과 연출에만 사용합니다.");
    }

    public static PlayerSkillDefinition Import(PlayerSkillAttackReceiver receiver)
    {
        if (receiver == null) throw new ArgumentNullException(nameof(receiver));
        var director = receiver.GetComponent<PlayableDirector>();
        var timeline = director.playableAsset as TimelineAsset;
        if (timeline == null) throw new InvalidOperationException("가져올 E Timeline이 없습니다.");
        var settings = receiver.GetComponent<PlayerSkillSequenceSettings>();
        var data = ScriptableObject.CreateInstance<PlayerSkillDefinition>();
        try
        {
        data.name = "PlayerSkill";
        data.duration = (float)(settings != null ? settings.GetContentEndTime(director) : timeline.duration);
        data.recoveryDuration = settings != null ? settings.RecoveryDuration : 0f;
        data.useInputCancelTime = false;
        var receiverObject = new SerializedObject(receiver);
        float shake = receiverObject.FindProperty("_skillHitShakeForce").floatValue;
        var core = receiver.GetComponentInParent<PlayerCore>();
        Transform root = core != null ? core.transform : receiver.transform.root;
        var animator = core != null && core.Animator != null ? core.Animator : root.GetComponentInChildren<Animator>(true);
        Quaternion facing = animator != null ? animator.transform.rotation : root.rotation;
        var inverse = Quaternion.Inverse(facing);
        int executionOrder = 0;
        foreach (var track in PlayerSkillStudio.AllTracks(timeline).OfType<PlayerSkillHitTrack>())
        {
            var trackReceiver = director.GetGenericBinding(track) as PlayerSkillAttackReceiver ?? receiver;
            var trackObject = new SerializedObject(trackReceiver);
            var fields = trackObject.FindProperty("_damageFields");
            var order = track.GetMarkers().OfType<PlayerSkillHitMarker>().Select((marker, index) => (marker, index))
                .ToDictionary(item => item.marker, item => executionOrder + item.index);
            executionOrder += order.Count;
            foreach (var marker in PlayerSkillTimelineUtility.OrderedHits(track))
            {
                var field = trackReceiver.GetDamageField(marker.DamageFieldNumber);
                if (field?.Hitbox == null) throw new InvalidOperationException($"{marker.name}의 Hitbox 연결을 확인하세요.");
                var serialized = fields.GetArrayElementAtIndex(marker.DamageFieldNumber - 1);
                var hit = new PlayerSkillHit
                {
                    name = marker.name,
                    executionOrder = order[marker],
                    time = (float)marker.time,
                    enabled = marker.Enabled && !track.mutedInHierarchy,
                    damage = field.Damage,
                    stagger = field.StaggerLevel,
                    hitStopFrames = field.HitStopFrame,
                    hitStopMode = field.HitStopMode,
                    skillGaugeAdditive = field.SkillGaugeAdditive,
                    targetLayers = serialized.FindPropertyRelative("_targetLayers").intValue,
                    triggerInteraction = (QueryTriggerInteraction)serialized.FindPropertyRelative("_triggerInteraction").enumValueIndex,
                    shakeForce = shake,
                    euler = (inverse * field.Hitbox.transform.rotation).eulerAngles
                };
                var collider = field.Hitbox;
                Vector3 scale = collider.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (collider is BoxCollider box)
                {
                    hit.shape = PlayerSkillHitShape.Box;
                    hit.position = inverse * (box.transform.TransformPoint(box.center) - root.position);
                    hit.size = Vector3.Scale(box.size, scale);
                }
                else if (collider is SphereCollider sphere)
                {
                    hit.shape = PlayerSkillHitShape.Sphere;
                    hit.position = inverse * (sphere.transform.TransformPoint(sphere.center) - root.position);
                    hit.radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
                }
                else if (collider is CapsuleCollider capsule)
                {
                    hit.shape = PlayerSkillHitShape.Capsule;
                    hit.position = inverse * (capsule.transform.TransformPoint(capsule.center) - root.position);
                    hit.capsuleDirection = capsule.direction;
                    float axis = capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y;
                    float radial = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) : capsule.direction == 2 ? Mathf.Max(scale.x, scale.y) : Mathf.Max(scale.x, scale.z);
                    hit.radius = capsule.radius * radial;
                    hit.height = Mathf.Max(hit.radius * 2f, capsule.height * axis);
                }
                else throw new InvalidOperationException("지원하지 않는 E 히트박스: " + collider.GetType().Name);
                data.hits.Add(hit);
            }
        }
        foreach (var track in PlayerSkillStudio.AllTracks(timeline).OfType<TimelineActivationTrack>())
        {
            if (director.GetGenericBinding(track) is not PlayerSkillImpactFrame) continue;
            foreach (var clip in track.GetClips())
                data.impactFrames.Add(new PlayerSkillImpactWindow
                { name = clip.displayName, enabled = !track.mutedInHierarchy, start = (float)clip.start, duration = (float)clip.duration });
        }
        if (animator != null) data.motion = ExtractMotion(timeline, director, animator, data.Duration);
        data.Normalize();
        return data;
        }
        catch { Object.DestroyImmediate(data); throw; }
    }

    private static bool Equivalent(PlayerSkillDefinition a, PlayerSkillDefinition b)
    {
        // World-to-local subtraction at different scene origins introduces tiny float errors.
        // Compare rounded temporary copies, preserving meaningful overrides and source data.
        var left = Object.Instantiate(a); var right = Object.Instantiate(b);
        try
        {
            foreach (var data in new[] { left, right })
                foreach (var hit in data.hits)
                {
                    if (hit == null) continue;
                    hit.position = Round(hit.position); hit.euler = Round(hit.euler); hit.size = Round(hit.size);
                    hit.radius = (float)Math.Round(hit.radius, 4); hit.height = (float)Math.Round(hit.height, 4);
                }
            return JsonUtility.ToJson(left) == JsonUtility.ToJson(right);
        }
        finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
    }
    private static Vector3 Round(Vector3 value) => new((float)Math.Round(value.x, 4), (float)Math.Round(value.y, 4), (float)Math.Round(value.z, 4));

    // Evaluate only the actual model's animation tracks on a disposable model.
    // No signals, camera, damage, scene VFX or original Timeline assets are evaluated or changed.
    public static PlayerSkillMotion ExtractMotion(TimelineAsset source, PlayableDirector sourceDirector, Animator sourceAnimator, float duration)
    {
        GameObject holder = null;
        TimelineAsset timeline = null;
        var assets = new List<Object>();
        try
        {
            holder = UltimatePreviewModel.Create(sourceAnimator, out var model, out var animator);
            holder.name = "E root extraction (temporary)";
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.hideFlags = HideFlags.HideAndDontSave;
            var director = holder.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.playableAsset = timeline;
            CopyAnimationTracks(source, sourceDirector, sourceAnimator, timeline, director, animator, assets);
            animator.applyRootMotion = true;
            director.Play();
            director.time = 0d;
            director.Evaluate();
            // Timeline uses absolute root transforms in Edit Mode previews. The import
            // needs the same deltas that the game's OnAnimatorMove consumes instead.
            for (int i = 0; i < director.playableGraph.GetRootPlayableCount(); i++)
                UseRootDeltas(director.playableGraph.GetRootPlayable(i));
            director.Evaluate();
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            int count = Mathf.Max(2, Mathf.CeilToInt(duration * 120f));
            var distances = new List<Keyframe> { new(0f, 0f) };
            float total = 0f, previous = 0f;
            for (int i = 1; i <= count; i++)
            {
                float time = duration * i / count;
                director.playableGraph.Evaluate(time - previous);
                total += animator.deltaPosition.z;
                distances.Add(new Keyframe(time / duration, total));
                previous = time;
            }
            var keys = distances.Select(key => new Keyframe(key.time, Mathf.Abs(total) > .00001f ? key.value / total : key.time)).ToArray();
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return new PlayerSkillMotion { enabled = true, start = 0f, end = duration, distance = total, curve = curve };
        }
        finally
        {
            if (holder != null) { var director = holder.GetComponent<PlayableDirector>(); if (director != null) director.Stop(); Object.DestroyImmediate(holder); }
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            if (timeline != null) Object.DestroyImmediate(timeline);
        }
    }

    /// <summary>Copies animation authoring, including offsets, masks, extrapolation and blend curves.
    /// Clip assets are read-only shared references; track/clip authoring containers are private copies.</summary>
    public static void CopyAnimationTracks(TimelineAsset source, PlayableDirector sourceDirector, Animator sourceAnimator,
        TimelineAsset target, PlayableDirector targetDirector, Animator targetAnimator, List<Object> owned)
    {
        var copied = new Dictionary<AnimationTrack, AnimationTrack>();
        foreach (var track in PlayerSkillStudio.AllTracks(source).OfType<AnimationTrack>())
        {
            var root = track;
            while (root.parent is AnimationTrack parent) root = parent;
            if (track.mutedInHierarchy || sourceDirector.GetGenericBinding(root) != sourceAnimator) continue;
            var parentCopy = track.parent is AnimationTrack parentTrack && copied.TryGetValue(parentTrack, out var found) ? found : null;
            var copy = target.CreateTrack<AnimationTrack>(parentCopy, track.name);
            owned.Add(copy);
            EditorUtility.CopySerialized(track, copy);
            copy.hideFlags = HideFlags.HideAndDontSave;
            var so = new SerializedObject(copy);
            so.FindProperty("m_Parent").objectReferenceValue = parentCopy != null ? parentCopy : target;
            so.FindProperty("m_Children").ClearArray();
            var markers = so.FindProperty("m_Markers.m_Objects");
            if (markers != null) markers.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
            var originalClips = track.GetClips().ToArray();
            var privateClips = copy.GetClips().ToArray();
            if (privateClips.Any(clip => originalClips.Any(original => ReferenceEquals(original, clip))))
                throw new InvalidOperationException("Timeline animation copy did not isolate clip authoring.");
            var parentField = typeof(TimelineClip).GetField("m_ParentTrack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var clip in privateClips) parentField.SetValue(clip, copy);
            copied.Add(track, copy);
            if (parentCopy == null) targetDirector.SetGenericBinding(copy, targetAnimator);
        }
        if (copied.Count == 0) throw new InvalidOperationException("플레이어 Animator에 연결된 E 애니메이션 트랙이 없습니다.");
    }

    private static void UseRootDeltas(Playable playable)
    {
        if (!playable.IsValid()) return;
        var type = playable.GetPlayableType();
        if (type.FullName == "UnityEngine.Animations.AnimationMotionXToDeltaPlayable")
        {
            var boxed = Activator.CreateInstance(type,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { playable.GetHandle() }, null);
            var method = type.GetMethod("SetAbsoluteMotion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("Timeline root-motion delta conversion is unavailable.");
            method.Invoke(boxed, new object[] { false });
        }
        for (int i = 0; i < playable.GetInputCount(); i++) UseRootDeltas(playable.GetInput(i));
    }
}
