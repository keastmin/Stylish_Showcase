using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-8000)]
public class PlayerInputCollector : MonoBehaviour
{
    private string _moveName = "Move";
    private string _attackName = "Attack";
    private string _dodgeName = "Dodge";
    private string _skillName = "Skill";
    private string _ultimateName = "Ultimate";

    private PlayerInput _playerInput;

    private InputAction _moveAction;
    private InputAction _attackAction;
    private InputAction _dodgeAction;
    private InputAction _skillAction;
    private InputAction _ultimateAction;

    private Vector2 _moveValue = Vector2.zero;
    private bool _attackValue = false;
    private bool _dodgeValue = false;
    private bool _skillValue = false;
    private bool _ultimateValue;

    // 프로퍼티
    public Vector2 MoveValue => _moveValue;
    public bool IsInputMove => MoveValue.sqrMagnitude >= 0.001f;
    public bool IsInputAttack => _attackValue;
    public bool IsInputDodge => _dodgeValue;
    public bool IsInputSkill => _skillValue;
    public bool IsInputUltimate => _ultimateValue;

    private void Awake()
    {
        TryGetComponent(out _playerInput);
        _moveAction = _playerInput.actions[_moveName];
        _attackAction = _playerInput.actions[_attackName];
        _dodgeAction = _playerInput.actions[_dodgeName];
        _skillAction = _playerInput.actions[_skillName];
        _ultimateAction = _playerInput.actions.FindAction(_ultimateName);
    }

    private void Update()
    {
        if (VirtualCutscenePlayback.InputBlocked || ProjectR.UI.PauseSettingsController.InputBlocked)
        {
            _moveValue = Vector2.zero;
            _attackValue = _dodgeValue = _skillValue = _ultimateValue = false;
            return;
        }
        // 움직임 입력 감지
        _moveValue = _moveAction.ReadValue<Vector2>();

        // 공격 입력 감지
        _attackValue = _attackAction.WasPressedThisFrame();

        // 회피 입력 감지
        _dodgeValue = _dodgeAction.WasPressedThisFrame();

        // 스킬 입력 감지
        _skillValue = _skillAction.WasPressedThisFrame();
        _ultimateValue = _ultimateAction != null && _ultimateAction.WasPressedThisFrame();
    }
}
