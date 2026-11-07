using UnityEditor;

public sealed class PlayerBasicAttackStudio : PlayerAttackStudio
{
    protected override bool OrdinaryDodge => false;
    [MenuItem("Tools/Character/Player/Basic Attack Studio")]
    public static void Open() => GetWindow<PlayerBasicAttackStudio>("Basic Attack Studio").Show();
}
