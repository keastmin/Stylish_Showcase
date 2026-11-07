#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

// Only created by PlayerSkillHitVerification, far outside the gameplay area.
[AddComponentMenu("")]
public sealed class PlayerSkillHitTestActor : MonoBehaviour, IDamageable, IHitStopParticipant
{
    public readonly List<DamageData> Hits = new();
    public PlayableDirector Director;
    public bool RejectDamage;
    public bool IsStopped;
    public bool IsHitStopped => IsStopped;
    public int DamageAttempts;
    public int StopCount;
    public float Health = 1000f;

    public bool TryTakeDamage(DamageData data)
    {
        DamageAttempts++;
        if (RejectDamage)
            return false;
        Hits.Add(data);
        Health -= data.DamageAmount;
        return true;
    }

    public void BeginHitStop()
    {
        IsStopped = true;
        StopCount++;
        SetDirectorSpeed(0);
    }

    public void EndHitStop()
    {
        IsStopped = false;
        SetDirectorSpeed(1);
    }

    private void SetDirectorSpeed(double speed)
    {
        if (Director == null || !Director.playableGraph.IsValid())
            return;
        for (int i = 0; i < Director.playableGraph.GetRootPlayableCount(); i++)
            Director.playableGraph.GetRootPlayable(i).SetSpeed(speed);
    }
}
#endif
