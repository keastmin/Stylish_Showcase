using System;
using UnityEngine;
using UnityEngine.Playables;

// Unsaved graph-owned guard also observes a paused Director's component lifetime.
[ExecuteAlways, AddComponentMenu("")]
public sealed class TimelineActivationLifetime : MonoBehaviour
{
    [NonSerialized] public PlayableDirector Director;
    [NonSerialized] public Action Release;

    private void OnDisable() => Release?.Invoke();

    private void LateUpdate()
    {
        if (Director == null || !Director.isActiveAndEnabled)
            Release?.Invoke();
    }
}
