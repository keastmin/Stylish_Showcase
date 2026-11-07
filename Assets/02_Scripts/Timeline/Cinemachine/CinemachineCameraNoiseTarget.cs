using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, AddComponentMenu("Timeline/Cinemachine Camera Noise Target")]
public sealed class CinemachineCameraNoiseTarget : MonoBehaviour
{
    [SerializeField] private TimelineCameraNoiseExtension _extension;
    private readonly HashSet<object> _owners = new();
    public TimelineCameraNoiseExtension Extension => _extension;

    public void Configure(TimelineCameraNoiseExtension extension)
    {
        ReleaseAll();
        _extension = extension;
    }

    public void SetOffset(object owner, Vector3 offset)
    {
        if (!isActiveAndEnabled || _extension == null)
        {
            Release(owner);
            return;
        }
        _owners.Add(owner);
        _extension.SetOffset(owner, offset);
    }

    public void Release(object owner)
    {
        if (_extension != null)
            _extension.Release(owner);
        _owners.Remove(owner);
    }

    private void ReleaseAll()
    {
        if (_extension != null)
            foreach (object owner in _owners)
                _extension.Release(owner);
        _owners.Clear();
    }
    private void OnDisable() => ReleaseAll();
    private void OnDestroy() => ReleaseAll();
    private void OnValidate() => ReleaseAll();
}
