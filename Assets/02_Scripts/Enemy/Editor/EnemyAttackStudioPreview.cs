using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

internal enum EnemyAttackPreviewMotion { AttackWarp, InPlace, AuthoredRootMotion }

/// <summary>A disposable visual-only copy of an enemy, owned by the attack editor.</summary>
internal sealed class EnemyAttackStudioPreview : IDisposable
{
    private PreviewRenderUtility _render;
    private GameObject _holder, _model, _stage, _target;
    private Animator _animator, _sourceAnimator;
    private Transform _hitboxOrigin;
    private Matrix4x4 _fallbackHitboxMatrix = Matrix4x4.identity;
    private PlayableGraph _graph;
    private AnimationClipPlayable _playable;
    private AnimationClip _clip;
    private Texture _lastTexture;
    private readonly List<Material> _materials = new();
    private readonly List<LineRenderer> _lines = new();
    private readonly Dictionary<AnimationClip, UltimateSequenceClip> _roots = new();
    private Material _gridMaterial, _damageMaterial, _selectedMaterial, _noticeMaterial, _warpMaterial, _limitMaterial, _originMaterial;
    private int _usedLines;
    private bool _dragging;
    private bool _frameRequested = true;
    private Vector3 _focus = new Vector3(0, 1, .6f);

    public bool Ready => _render != null && _animator != null && _holder != null;
    public Animator ModelAnimator => _animator;
    public Vector3 RootPosition { get; private set; }
    public float SampledTime { get; private set; }
    public AnimationClip SampledClip => _clip;
    public float PlannedWarpDistance { get; private set; }
    public float AuthoredPlanarTravel { get; private set; }
    public float yaw = 110, pitch = 25, distance = 7;

    public void Build(EnemyCore enemy)
    {
        Dispose();
        if (enemy == null) return;
        _sourceAnimator = enemy.GetComponentInChildren<Animator>(true);
        if (_sourceAnimator == null) return;
        try
        {
            _render = new PreviewRenderUtility();
            _render.camera.fieldOfView = 38;
            _render.camera.nearClipPlane = .03f;
            _render.camera.farClipPlane = 200;
            _render.camera.backgroundColor = new Color(.055f, .072f, .10f);
            _render.camera.clearFlags = CameraClearFlags.SolidColor;
            _render.ambientColor = new Color(.35f, .38f, .43f);
            _render.lights[0].intensity = 1.6f;
            _render.lights[0].transform.rotation = Quaternion.Euler(35, 35, 0);
            _render.lights[1].intensity = 1.2f;
            _render.lights[1].transform.rotation = Quaternion.Euler(35, 215, 0);
            _holder = UltimatePreviewModel.Create(_sourceAnimator, out _model, out _animator);
            _holder.name = "Enemy Attack Studio - disposable model";
            _holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _animator.speed = 1;
            _animator.fireEvents = false;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            foreach (var skin in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.forceMatrixRecalculationPerRender = true;
            }
            // The current Striker has an authored debug cube under its model.
            // Show only the data-driven wire fields made by this preview.
            foreach (var child in _model.GetComponentsInChildren<Transform>(true))
                if (child.name == "Hitbox Preview") child.gameObject.SetActive(false);
            ResolveHitboxOrigin(enemy);
            _render.AddSingleGO(_holder);

            _stage = new GameObject("Enemy Attack Studio - disposable stage") { hideFlags = HideFlags.HideAndDontSave };
            _render.AddSingleGO(_stage);
            var floorMaterial = Material("Universal Render Pipeline/Lit", new Color(.11f, .14f, .18f));
            var targetMaterial = Material("Universal Render Pipeline/Lit", new Color(.18f, .62f, .78f));
            _gridMaterial = Material("Universal Render Pipeline/Unlit", new Color(.20f, .26f, .32f));
            _damageMaterial = Material("Universal Render Pipeline/Unlit", new Color(.78f, .19f, .23f));
            _selectedMaterial = Material("Universal Render Pipeline/Unlit", new Color(1, .38f, .30f));
            _noticeMaterial = Material("Universal Render Pipeline/Unlit", new Color(1, .72f, .20f));
            _warpMaterial = Material("Universal Render Pipeline/Unlit", new Color(.22f, .73f, .87f));
            _limitMaterial = Material("Universal Render Pipeline/Unlit", new Color(.72f, .48f, 1f));
            _originMaterial = Material("Universal Render Pipeline/Unlit", new Color(.8f, .85f, .92f));
            var floor = Primitive(PrimitiveType.Cube, "Ground", floorMaterial);
            floor.transform.localScale = new Vector3(100, .04f, 100);
            floor.transform.position = new Vector3(0, -.04f, 0);
            _target = Primitive(PrimitiveType.Capsule, "Distance reference target", targetMaterial);
            _target.transform.localScale = new Vector3(.7f, .9f, .7f);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void ResolveHitboxOrigin(EnemyCore enemy)
    {
        var pool = enemy.GetComponent<EnemyHitboxPool>();
        Transform sourceOrigin = pool != null
            ? new SerializedObject(pool).FindProperty("_hitboxContainer")?.objectReferenceValue as Transform
            : null;
        if (sourceOrigin == null) sourceOrigin = enemy.transform;
        if (sourceOrigin == _sourceAnimator.transform) _hitboxOrigin = _model.transform;
        else if (sourceOrigin.IsChildOf(_sourceAnimator.transform))
            _hitboxOrigin = _model.transform.Find(AnimationUtility.CalculateTransformPath(sourceOrigin, _sourceAnimator.transform));
        var inverseRotation = Quaternion.Inverse(_sourceAnimator.transform.rotation);
        _fallbackHitboxMatrix = Matrix4x4.TRS(
            inverseRotation * (sourceOrigin.position - _sourceAnimator.transform.position),
            inverseRotation * sourceOrigin.rotation, sourceOrigin.lossyScale);
    }

    private Material Material(string shaderName, Color color)
    {
        var shader = Shader.Find(shaderName) ?? Shader.Find("Hidden/Internal-Colored");
        if (shader == null) throw new InvalidOperationException("미리보기용 셰이더를 찾을 수 없습니다.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = color };
        _materials.Add(material);
        return material;
    }

    private GameObject Primitive(PrimitiveType type, string name, Material material)
    {
        var item = GameObject.CreatePrimitive(type);
        item.name = name;
        item.hideFlags = HideFlags.HideAndDontSave;
        item.transform.SetParent(_stage.transform, false);
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        var renderer = item.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return item;
    }

    public void SamplePose(AnimationClip clip, float time, bool showRootMotion)
    {
        if (!Ready || clip == null) return;
        if (_clip != clip || !_graph.IsValid())
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graph = PlayableGraph.Create("Enemy Attack Studio preview");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _playable = AnimationClipPlayable.Create(_graph, clip);
            _playable.SetSpeed(0);
            _playable.SetApplyFootIK(false);
            _playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(_graph, "Enemy visual model", _animator);
            output.SetSourcePlayable(_playable);
            _graph.Play();
            _clip = clip;
        }
        SampledTime = Mathf.Clamp(time, 0, clip.length);
        _playable.SetTime(SampledTime);
        _graph.Evaluate(0);
        RootPosition = Vector3.zero;
        if (showRootMotion)
        {
            var root = GetRoot(clip);
            RootPosition = UltimateSequenceUtility.SampleRoot(root, SampledTime).position;
            RootPosition = new Vector3(RootPosition.x, 0, RootPosition.z);
        }
        _holder.transform.position = RootPosition;
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
    }

    private UltimateSequenceClip GetRoot(AnimationClip clip)
    {
        if (!_roots.TryGetValue(clip, out var root))
        {
            root = new UltimateSequenceClip { clip = clip, rootSamples = UltimateRootMotionBaker.Extract(clip, _sourceAnimator) };
            _roots.Add(clip, root);
        }
        return root;
    }

    // Same forward-distance cap as targeting a stationary point directly ahead.
    // Runtime collider contact, moving targets, and Animator blending are excluded.
    internal static float EstimateWarpDistance(EnemyAttackSO attack, float targetDistance)
    {
        if (attack == null) return 0;
        float offset = attack.HitboxInfo != null && attack.HitboxInfo.Length > 0 ? attack.HitboxInfo[0].Offset.z : 0;
        return Mathf.Min(Mathf.Max(0, targetDistance - offset), Mathf.Max(0, attack.AttackWarpDistance));
    }

    internal static float PlanarTravel(UltimateSequenceClip root, float time)
    {
        float travel = 0;
        var samples = root.rootSamples;
        for (int i = 1; i < samples.Count; i++)
        {
            var a = samples[i - 1];
            var b = samples[i];
            if (time <= a.time) break;
            Vector3 delta = b.position - a.position;
            delta.y = 0;
            float fraction = b.time > a.time ? Mathf.Clamp01((time - a.time) / (b.time - a.time)) : 1;
            travel += delta.magnitude * fraction;
            if (time < b.time) break;
        }
        return travel;
    }

    public void SampleAttackPose(AnimationClip clip, float time, EnemyAttackSO attack, float targetDistance, EnemyAttackPreviewMotion motion)
    {
        SamplePose(clip, time, motion == EnemyAttackPreviewMotion.AuthoredRootMotion);
        if (!Ready || clip == null) return;
        PlannedWarpDistance = EstimateWarpDistance(attack, targetDistance);
        AuthoredPlanarTravel = motion == EnemyAttackPreviewMotion.AttackWarp ? PlanarTravel(GetRoot(clip), SampledTime) : 0;
        if (motion == EnemyAttackPreviewMotion.AttackWarp)
        {
            // WarpRootMotion scales each planar delta by the planned distance,
            // then clamps at the destination. Do not normalize to clip duration:
            // short/no-root clips must remain visibly short/stationary.
            RootPosition = Vector3.forward * (PlannedWarpDistance * Mathf.Clamp01(AuthoredPlanarTravel));
            _holder.transform.position = RootPosition;
        }
    }

    public void RequestFrame() => _frameRequested = true;

    private void FramePath(EnemyAttackSO attack, float targetDistance, float aspect)
    {
        float end = Mathf.Max(2, targetDistance, attack != null ? attack.GetMaximumAttackReach() : 0);
        float halfFov = _render.camera.fieldOfView * Mathf.Deg2Rad * .5f;
        _focus = new Vector3(0, 1, end * .5f);
        Vector3 extents = new Vector3(1.2f, 1.4f, end * .5f + .8f);
        if (attack != null && attack.HitboxInfo != null)
            foreach (var hit in attack.HitboxInfo)
            {
                extents.x = Mathf.Max(extents.x, Mathf.Abs(hit.Offset.x) + Mathf.Abs(hit.Size.x) * .5f + .3f);
                extents.y = Mathf.Max(extents.y, Mathf.Abs(hit.Offset.y - _focus.y) + Mathf.Abs(hit.Size.y) * .5f + .3f);
            }
        var inverse = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0));
        distance = 3;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = inverse * new Vector3((i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z);
            // Fit the actual camera-space bounds with room for overlay labels.
            distance = Mathf.Max(distance, Mathf.Abs(corner.x) / (Mathf.Tan(halfFov) * aspect * .85f) - corner.z,
                Mathf.Abs(corner.y) / (Mathf.Tan(halfFov) * .7f) - corner.z);
        }
        _frameRequested = false;
    }

    public void Draw(Rect rect, AnimationClip clip, float time, EnemyAttackSO attack, int selectedHitbox,
        bool showNotice, bool showAll, bool hitActive, float targetDistance, EnemyAttackPreviewMotion motion)
    {
        if (!Ready || rect.width < 10 || rect.height < 10) return;
        HandleInput(rect);
        if (Event.current.type != EventType.Repaint) return;
        SampleAttackPose(clip, time, attack, targetDistance, motion);
        UpdateGeometry(attack, selectedHitbox, showNotice, showAll, hitActive, targetDistance, motion == EnemyAttackPreviewMotion.AttackWarp);
        if (_frameRequested) FramePath(attack, targetDistance, rect.width / rect.height);
        var rotation = Quaternion.Euler(pitch, yaw, 0);
        _render.camera.farClipPlane = Mathf.Max(200, distance * 3);
        _render.camera.transform.SetPositionAndRotation(_focus - rotation * Vector3.forward * distance, rotation);
        _render.BeginPreview(rect, GUIStyle.none);
        Texture texture;
        try { _render.Render(true); }
        finally { texture = _render.EndPreview(); }
        _lastTexture = texture;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        if (motion == EnemyAttackPreviewMotion.AttackWarp && attack != null)
        {
            WorldLabel(rect, Vector3.zero, "출발 0m", Color.white, -24);
            WorldLabel(rect, Vector3.forward * PlannedWarpDistance, $"예상 도착 {PlannedWarpDistance:0.00}m", new Color(.3f, .9f, 1), 8);
            WorldLabel(rect, Vector3.forward * Mathf.Max(0, attack.AttackWarpDistance) + Vector3.right * .7f,
                $"최대 돌진 {Mathf.Max(0, attack.AttackWarpDistance):0.00}m", new Color(.8f, .65f, 1), -24);
        }
    }

    private void WorldLabel(Rect rect, Vector3 point, string text, Color color, float offsetY)
    {
        Vector3 viewport = _render.camera.WorldToViewportPoint(point + Vector3.up * .12f);
        if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return;
        var style = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = color;
        float width = style.CalcSize(new GUIContent(text)).x + 12;
        float x = Mathf.Clamp(rect.x + viewport.x * rect.width - width * .5f, rect.x + 4, Mathf.Max(rect.x + 4, rect.xMax - width - 4));
        float y = Mathf.Clamp(rect.y + (1 - viewport.y) * rect.height + offsetY, rect.y + 90, rect.yMax - 45);
        var label = new Rect(x, y, width, 20);
        EditorGUI.DrawRect(label, new Color(.035f, .045f, .065f, .9f));
        GUI.Label(label, text, style);
    }

    internal void Orbit(Vector2 delta)
    {
        yaw += delta.x * .45f;
        pitch = Mathf.Clamp(pitch + delta.y * .35f, -10, 85);
    }

    private void HandleInput(Rect rect)
    {
        var evt = Event.current;
        int control = GUIUtility.GetControlID("EnemyAttackPreviewOrbit".GetHashCode(), FocusType.Passive, rect);
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && (evt.button == 0 || evt.button == 1))
        {
            _dragging = true;
            GUIUtility.hotControl = control;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _dragging && GUIUtility.hotControl == control)
        {
            Orbit(evt.delta);
            GUI.changed = true;
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && _dragging)
        {
            _dragging = false;
            if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            evt.Use();
        }
        else if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
        {
            distance = Mathf.Clamp(distance * Mathf.Exp(evt.delta.y * .06f), 1.5f, 500);
            GUI.changed = true;
            evt.Use();
        }
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Orbit);
    }

    private void UpdateGeometry(EnemyAttackSO attack, int selected, bool notice, bool all, bool active, float targetDistance, bool warp)
    {
        _usedLines = 0;
        for (int i = -12; i <= 12; i++)
        {
            Line(new[] { new Vector3(i, -.012f, -12), new Vector3(i, -.012f, 12) }, _gridMaterial, .008f);
            Line(new[] { new Vector3(-12, -.012f, i), new Vector3(12, -.012f, i) }, _gridMaterial, .008f);
        }
        _target.transform.position = new Vector3(0, .9f, Mathf.Max(0, targetDistance));
        Line(new[] { Vector3.up * .015f, Vector3.forward * Mathf.Max(0, targetDistance) + Vector3.up * .015f }, _warpMaterial, .018f);
        if (warp && attack != null) DrawWarpPath(attack);
        Circle(RootPosition + Vector3.up * .02f, .3f, Vector3.right, Vector3.forward, _warpMaterial, .025f);
        // Forward arrow makes the local Z direction visible at any camera angle.
        var tip = RootPosition + new Vector3(0, .035f, .85f);
        Line(new[] { RootPosition + Vector3.up * .035f, tip }, _warpMaterial, .025f);
        Line(new[] { tip + new Vector3(-.13f, 0, -.18f), tip, tip + new Vector3(.13f, 0, -.18f) }, _warpMaterial, .025f);
        if (attack != null && attack.HitboxInfo != null)
        {
            Matrix4x4 damageMatrix = _hitboxOrigin != null ? _hitboxOrigin.localToWorldMatrix
                : Matrix4x4.Translate(RootPosition) * _fallbackHitboxMatrix;
            for (int i = 0; i < attack.HitboxInfo.Length; i++)
            {
                if (!all && i != selected) continue;
                var hit = attack.HitboxInfo[i];
                var color = i == selected || active ? _selectedMaterial : _damageMaterial;
                DrawHit(hit, damageMatrix, false, color, (i == selected ? .025f : .015f) * (active ? 1.5f : 1f));
                if (warp)
                    DrawHit(hit, Matrix4x4.Translate(Vector3.forward * PlannedWarpDistance - RootPosition) * damageMatrix, false, _warpMaterial, .012f);
                // Notice targeting works in root/world units (independent of model scale).
                if (notice) DrawHit(hit, Matrix4x4.Translate(warp ? Vector3.forward * PlannedWarpDistance : RootPosition), true, _noticeMaterial, .012f);
            }
        }
        for (int i = _usedLines; i < _lines.Count; i++) _lines[i].enabled = false;
    }

    private void DrawWarpPath(EnemyAttackSO attack)
    {
        Vector3 start = Vector3.up * .045f;
        Vector3 end = start + Vector3.forward * PlannedWarpDistance;
        Vector3 limit = start + Vector3.forward * Mathf.Max(0, attack.AttackWarpDistance);
        Circle(start, .3f, Vector3.right, Vector3.forward, _originMaterial, .025f);
        Circle(end, .3f, Vector3.right, Vector3.forward, _warpMaterial, .035f);
        Line(new[] { start + Vector3.left * .2f, end + Vector3.left * .2f }, _warpMaterial, .015f);
        Line(new[] { start + Vector3.right * .2f, end + Vector3.right * .2f }, _warpMaterial, .015f);
        Line(new[] { start + Vector3.up * .01f, RootPosition + Vector3.up * .055f }, _warpMaterial, .06f);
        Line(new[] { end, limit }, _limitMaterial, .018f);
        Line(new[] { limit + Vector3.left * .7f, limit + Vector3.right * .7f }, _limitMaterial, .04f);
        Circle(limit, .22f, Vector3.right, Vector3.forward, _limitMaterial, .02f);
        float maximum = Mathf.Max(0, attack.AttackWarpDistance);
        float step = Mathf.Max(1, Mathf.Ceil(maximum / 50));
        for (float z = step; z < maximum; z += step)
            Line(new[] { new Vector3(-.1f, .045f, z), new Vector3(.1f, .045f, z) }, _originMaterial, .014f);
    }

    private void DrawHit(EnemyAttackHitboxInfo hit, Matrix4x4 matrix, bool notice, Material material, float width)
    {
        Vector3 size = Abs(hit.Size);
        Vector3 center = matrix.MultiplyPoint3x4(hit.Offset);
        if (hit.HitboxType == EnemyAttackHitboxType.Sphere)
        {
            Vector3 scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
            float radius;
            if (notice)
            {
                radius = Mathf.Max(size.x, size.y, size.z) * .5f;
                if (radius <= .001f) radius = Mathf.Max(0, hit.Radius.x);
                Vector3 additional = Abs(hit.AdditiveNoticeRange);
                radius = Mathf.Max(.001f, radius + Mathf.Max(additional.x, additional.y, additional.z) * .5f);
            }
            else if (size.sqrMagnitude > .000001f)
            {
                var worldSize = Vector3.Scale(size, scale);
                radius = Mathf.Max(worldSize.x, worldSize.y, worldSize.z) * .5f;
            }
            else radius = Mathf.Max(0, hit.Radius.x) * Mathf.Max(scale.x, scale.y, scale.z);
            Circle(center, radius, Vector3.right, Vector3.up, material, width);
            Circle(center, radius, Vector3.right, Vector3.forward, material, width);
            Circle(center, radius, Vector3.forward, Vector3.up, material, width);
        }
        else
        {
            if (notice) size = Vector3.Max(size + Abs(hit.AdditiveNoticeRange), Vector3.one * .002f);
            var h = size * .5f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = matrix.MultiplyPoint3x4(hit.Offset + new Vector3(
                (i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            Line(new[] { corners[0], corners[1], corners[5], corners[4], corners[0] }, material, width);
            Line(new[] { corners[2], corners[3], corners[7], corners[6], corners[2] }, material, width);
            for (int i = 0; i < 8; i++)
                if ((i & 2) == 0) Line(new[] { corners[i], corners[i | 2] }, material, width);
        }
    }

    private void Circle(Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Material material, float width)
    {
        var points = new Vector3[65];
        for (int i = 0; i < points.Length; i++)
        {
            float a = i * Mathf.PI * 2 / 64;
            points[i] = center + (axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a)) * radius;
        }
        Line(points, material, width);
    }

    private void Line(Vector3[] positions, Material material, float width)
    {
        if (_usedLines == _lines.Count)
        {
            var item = new GameObject("Preview field") { hideFlags = HideFlags.HideAndDontSave };
            item.transform.SetParent(_stage.transform, false);
            var renderer = item.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.numCapVertices = 2;
            renderer.numCornerVertices = 2;
            _lines.Add(renderer);
        }
        var line = _lines[_usedLines++];
        line.enabled = true;
        line.sharedMaterial = material;
        line.startColor = line.endColor = Color.white;
        line.startWidth = line.endWidth = width;
        line.positionCount = positions.Length;
        line.SetPositions(positions);
    }

    private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    public void SaveLastFrame(string path)
    {
        if (_lastTexture == null || string.IsNullOrEmpty(path)) return;
        var previous = RenderTexture.active;
        var buffer = RenderTexture.GetTemporary(_lastTexture.width, _lastTexture.height, 0, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        try
        {
            Graphics.Blit(_lastTexture, buffer);
            RenderTexture.active = buffer;
            image = new Texture2D(buffer.width, buffer.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, buffer.width, buffer.height), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
        }
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        _render?.Cleanup();
        _render = null;
        // Build can fail before objects are registered with PreviewRenderUtility.
        if (_holder != null) UnityEngine.Object.DestroyImmediate(_holder);
        if (_stage != null) UnityEngine.Object.DestroyImmediate(_stage);
        foreach (var material in _materials)
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        _materials.Clear();
        _lines.Clear();
        _roots.Clear();
        _holder = _model = _stage = _target = null;
        _animator = _sourceAnimator = null;
        _hitboxOrigin = null;
        _clip = null;
        _lastTexture = null;
        _dragging = false;
        _frameRequested = true;
        PlannedWarpDistance = AuthoredPlanarTravel = 0;
        RootPosition = Vector3.zero;
        SampledTime = 0;
    }
}
