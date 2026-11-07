using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(PlayerCore)), DefaultExecutionOrder(1000)]
public sealed class PlayerDodgeScreenEffects : MonoBehaviour
{
    private PlayerCore _core;
    private DodgeScreenOutput _output;
    private void Awake() { _core = GetComponent<PlayerCore>(); }
    private void LateUpdate()
    {
        if (_core.DodgeDefinition == null) { OnDisable(); return; }
        if (_output == null) _output = new DodgeScreenOutput(_core.MainCamera);
        _output.Set(_core.Dodge?.Sequence, _core.MainCamera);
    }
    private void OnDisable() { _output?.Dispose(); _output = null; }
}
