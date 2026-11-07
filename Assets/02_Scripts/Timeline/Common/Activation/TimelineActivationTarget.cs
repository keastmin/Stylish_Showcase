using UnityEngine;
using UnityEngine.Timeline;

// Adapters expose one boolean resource; the common registry owns its lifecycle.
[ExecuteAlways]
public abstract class TimelineActivationTarget : MonoBehaviour
{
    protected abstract Object Resource { get; }
    protected abstract bool ReadActive(Object resource);
    protected abstract void WriteActive(Object resource, bool active);

    public bool IsConfigured => Resource != null;

    public void RegisterOwner(object owner) => SetRequestedActive(owner, false);

    public void SetRequestedActive(object owner, bool active)
    {
        Object resource = Resource;
        if (isActiveAndEnabled && resource != null)
            TimelineActivationRegistry.Request(this, resource, owner, active);
        else
            ReleaseOwner(owner);
    }

    public void ReleaseOwner(object owner) => TimelineActivationRegistry.Release(this, owner);

    // Component adapters can register preview properties with Timeline here.
    // ScriptableObject adapters use registry restoration, including editor saves.
    public virtual void GatherProperties(IPropertyCollector collector) { }

    internal bool ReadResource(Object resource) => ReadActive(resource);
    internal void WriteResource(Object resource, bool active) => WriteActive(resource, active);

    protected virtual void OnDisable() => TimelineActivationRegistry.ReleaseTarget(this);
    protected virtual void OnDestroy() => TimelineActivationRegistry.ReleaseTarget(this);
    protected virtual void OnValidate() => TimelineActivationRegistry.ReleaseTarget(this);
}
