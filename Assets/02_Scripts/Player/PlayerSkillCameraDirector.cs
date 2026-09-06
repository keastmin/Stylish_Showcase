using Unity.Cinemachine;
using UnityEngine;

public class PlayerSkillCameraDirector : MonoBehaviour
{
    [SerializeField] private Transform _modelTransform;
    [SerializeField] private CinemachineCamera _skillCinemachine1;

    private Vector3 _originLocalPos;
    private Quaternion _originLocalRot;

    private void Awake()
    {
        _originLocalPos = _skillCinemachine1.transform.localPosition;
        _originLocalRot = _skillCinemachine1.transform.localRotation;
    }

    public void CinemachineRemoveParent()
    {
        _skillCinemachine1.transform.parent = null;
    }

    public void CinemachineAddParent()
    {
        _skillCinemachine1.transform.parent = _modelTransform;
        _skillCinemachine1.transform.localPosition = _originLocalPos;
        _skillCinemachine1.transform.localRotation = _originLocalRot;
    }
}