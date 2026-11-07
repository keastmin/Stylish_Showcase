using UnityEditor;

public sealed class PlayerOrdinaryDodgeAttackStudio : PlayerAttackStudio
{
    protected override bool OrdinaryDodge => true;
    [MenuItem("Tools/Character/Player/Dodge Attack Studio")]
    public static void Open() => GetWindow<PlayerOrdinaryDodgeAttackStudio>("Dodge Attack Studio").Show();
}
