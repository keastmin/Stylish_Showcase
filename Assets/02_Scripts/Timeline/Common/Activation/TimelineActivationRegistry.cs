using System.Collections.Generic;
using UnityEngine;

public static class TimelineActivationRegistry
{
    private sealed class RequestState
    {
        public TimelineActivationTarget Target;
        public bool Active;
    }

    private sealed class ResourceState
    {
        public Object Resource;
        public TimelineActivationTarget Adapter;
        public bool OriginalActive;
        public readonly Dictionary<object, RequestState> Requests = new();
    }

    private static readonly Dictionary<Object, ResourceState> Resources = new();
    private static readonly Dictionary<object, ResourceState> Owners = new();
    private static bool _suspended;

    internal static void Request(TimelineActivationTarget target, Object resource, object owner, bool active)
    {
        if (owner == null)
            return;

        if (Owners.TryGetValue(owner, out ResourceState previous) &&
            (previous.Resource != resource || previous.Requests[owner].Target != target))
            Release(previous.Requests[owner].Target, owner);

        if (!Resources.TryGetValue(resource, out ResourceState state))
        {
            state = new ResourceState
            {
                Resource = resource,
                Adapter = target,
                OriginalActive = target.ReadResource(resource)
            };
            Resources.Add(resource, state);
        }

        if (!state.Requests.TryGetValue(owner, out RequestState request))
        {
            request = new RequestState { Target = target };
            state.Requests.Add(owner, request);
            Owners.Add(owner, state);
        }
        request.Active = active;
        Apply(state);
    }

    internal static void Release(TimelineActivationTarget target, object owner)
    {
        if (owner == null || !Owners.TryGetValue(owner, out ResourceState state) ||
            !ReferenceEquals(state.Requests[owner].Target, target))
            return;

        state.Requests.Remove(owner);
        Owners.Remove(owner);
        if (state.Requests.Count == 0)
        {
            // Remove first: restoring an adapter's resource may invoke OnDisable.
            Resources.Remove(state.Resource);
            Write(state, state.OriginalActive);
        }
        else
        {
            foreach (RequestState remaining in state.Requests.Values)
            {
                state.Adapter = remaining.Target;
                break;
            }
            Apply(state);
        }
    }

    internal static void ReleaseTarget(TimelineActivationTarget target)
    {
        var owned = new List<object>();
        foreach (var owner in Owners)
            if (ReferenceEquals(owner.Value.Requests[owner.Key].Target, target))
                owned.Add(owner.Key);
        foreach (object owner in owned)
            Release(target, owner);
    }

    private static void Apply(ResourceState state)
    {
        if (_suspended)
            return;
        bool active = false;
        foreach (RequestState request in state.Requests.Values)
            active |= request.Active;
        Write(state, active);
    }

    private static void Write(ResourceState state, bool active)
    {
        // The resource argument preserves the old reference even after rebinding.
        if (state.Resource != null && state.Adapter.ReadResource(state.Resource) != active)
            state.Adapter.WriteResource(state.Resource, active);
    }

    public static void RestoreAll()
    {
        var states = new List<ResourceState>(Resources.Values);
        Resources.Clear();
        Owners.Clear();
        _suspended = false;
        foreach (ResourceState state in states)
            Write(state, state.OriginalActive);
    }

    // Editor save hooks temporarily serialize the baseline, keeping requests so
    // a paused preview can resume its exact state after the save completes.
    public static void SuspendForSave()
    {
        _suspended = true;
        foreach (ResourceState state in new List<ResourceState>(Resources.Values))
            Write(state, state.OriginalActive);
    }

    public static void ResumeAfterSave()
    {
        _suspended = false;
        foreach (ResourceState state in new List<ResourceState>(Resources.Values))
            Apply(state);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode() => RestoreAll();
}
