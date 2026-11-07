using UnityEngine;
using UnityEngine.Rendering.Universal;

[AddComponentMenu("Timeline/Renderer Feature Activation Target")]
public class RendererFeatureActivationTarget : TimelineActivationTarget
{
    [SerializeField] private ScriptableRendererData _rendererData;
    [SerializeField] private ScriptableRendererFeature _feature;

    public ScriptableRendererData RendererData => _rendererData;
    public ScriptableRendererFeature Feature => _feature;

    protected override Object Resource => _rendererData != null && _feature != null &&
        _rendererData.rendererFeatures.Contains(_feature) ? _feature : null;

    protected override bool ReadActive(Object resource) =>
        ((ScriptableRendererFeature)resource).isActive;

    protected override void WriteActive(Object resource, bool active) =>
        ((ScriptableRendererFeature)resource).SetActive(active);

    public void Configure(ScriptableRendererData rendererData, ScriptableRendererFeature feature)
    {
        // Release while the old reference is still available.
        base.OnValidate();
        _rendererData = rendererData;
        _feature = feature;
    }
}
