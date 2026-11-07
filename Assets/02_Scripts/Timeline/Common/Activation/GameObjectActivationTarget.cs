using UnityEngine;
using UnityEngine.Timeline;

[AddComponentMenu("Timeline/Game Object Activation Target")]
public sealed class GameObjectActivationTarget : TimelineActivationTarget
{
    [SerializeField] private GameObject _target;
    public GameObject Target => _target;

    // An adapter must stay alive when its resource is switched off.
    protected override Object Resource => _target != null &&
        !transform.IsChildOf(_target.transform) ? _target : null;
    protected override bool ReadActive(Object resource) => ((GameObject)resource).activeSelf;
    protected override void WriteActive(Object resource, bool active) => ((GameObject)resource).SetActive(active);

    public void Configure(GameObject target)
    {
        base.OnValidate();
        _target = target;
    }

    public override void GatherProperties(IPropertyCollector collector)
    {
        if (_target != null)
            collector.AddFromName(_target, "m_IsActive");
    }
}
