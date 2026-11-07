using System;
using ProjectR.VFX;
using UnityEditor;
using UnityEngine;

internal readonly struct DodgeAfterimageFrame
{
    public readonly AnimationClip clip;
    public readonly float source, scale;
    public readonly Vector3 position;
    public readonly bool inDodge;
    public DodgeAfterimageFrame(AnimationClip clip, float source, float scale, Vector3 position, bool inDodge)
    { this.clip=clip; this.source=source; this.scale=scale; this.position=position; this.inDodge=inDodge; }
}

/// <summary>The actual VFX Graph and frozen poses, isolated in the tool's PreviewScene.</summary>
internal sealed class DodgeAfterimagePreview : IDisposable
{
    readonly PreviewRenderUtility render;
    readonly Transform model;
    readonly Action<AnimationClip,float,Vector3> samplePose;
    ChromaticAfterimageTrail effect;
    GameObject prefab;
    string signature;
    float simulatedTime=-1, targetTime, eventTime;
    bool started;
    public int Ghosts => effect!=null?effect.ActiveSnapshots:0;
    public int Particles => effect!=null && effect.particles!=null?Mathf.Max(0,effect.particles.aliveParticleCount):0;
    public bool CatchingUp => effect!=null && targetTime-simulatedTime>.0001f;
    public bool Enabled => effect!=null;
    public float SimulatedTime => simulatedTime;
    public ChromaticAfterimageTrail Effect => effect;

    public DodgeAfterimagePreview(PreviewRenderUtility render, Transform model, Action<AnimationClip,float,Vector3> samplePose)
    { this.render=render; this.model=model; this.samplePose=samplePose; }

    public void Reset()
    {
        if(effect!=null) effect.Clear();
        simulatedTime=-1; started=false;
    }

    public void Update(DodgeAfterimageSettings settings, bool visible, float time, float start,
        Func<float,DodgeAfterimageFrame> frame, bool playing)
    {
        if(!visible || settings==null) { DisposeEffect(); return; }
        var source=settings.prefab!=null?settings.prefab:AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
        var template=source!=null?source.GetComponent<ChromaticAfterimageTrail>():null;
        if(template==null || template.afterimageMaterial==null || template.particles==null || template.particles.visualEffectAsset==null)
        { DisposeEffect(); return; }
        if(effect==null || prefab!=source)
        {
            DisposeEffect();
            prefab=source;
            var instance=UnityEngine.Object.Instantiate(source);
            instance.name="Dodge Studio • actual VFX Graph preview";
            foreach(var t in instance.GetComponentsInChildren<Transform>(true))
            { t.gameObject.hideFlags=HideFlags.HideAndDontSave; t.gameObject.layer=model.gameObject.layer; }
            render.AddSingleGO(instance);
            effect=instance.GetComponent<ChromaticAfterimageTrail>();
            effect.externalClock=true; effect.externalRendering=true;
            settings.ApplyTo(effect); effect.Bind(model);
            effect.particles.pause=true; effect.particles.playRate=1;
            effect.particles.resetSeedOnPlay=false; effect.particles.startSeed=142;
            signature=null;
        }
        string next=JsonUtility.ToJson(settings);
        if(signature!=next)
        {
            bool rebind=effect.includeRigidMeshes!=settings.includeRigidMeshes || effect.maxSnapshots!=settings.maxSnapshots;
            settings.ApplyTo(effect);
            if(rebind) effect.Bind(model);
            signature=next; Reset();
        }
        if(time+.0001f<simulatedTime || Mathf.Abs(eventTime-start)>.0001f) Reset();
        eventTime=start;
        targetTime=Mathf.Max(0,time);
        if(simulatedTime<0)
        {
            simulatedTime=0;
            var initial=frame(0); samplePose(initial.clip,initial.source,initial.position);
            StartIfDue(settings,initial);
        }
        float remaining=targetTime-simulatedTime;
        if(remaining>.00001f)
        {
            // Normal playback follows the existing tool clock. A distant scrub replays
            // historical poses across editor frames so GPU emissions retain their origins.
            float step=playing && remaining<.5f?remaining:Mathf.Min(.05f,remaining);
            simulatedTime+=step;
            var pose=frame(simulatedTime);
            samplePose(pose.clip,pose.source,pose.position);
            StartIfDue(settings,pose);
            if(settings.stopOnDodgeExit && !pose.inDodge) effect.StopEmission();
            float delta=step*(settings.useRealTime?1:Mathf.Clamp01(pose.scale));
            effect.Tick(delta);
            effect.particles.Simulate(delta,1);
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    void StartIfDue(DodgeAfterimageSettings settings,DodgeAfterimageFrame pose)
    {
        if(started || eventTime<0 || simulatedTime+.00001f<eventTime || (settings.stopOnDodgeExit && !pose.inDodge)) return;
        effect.Play(Mathf.Max(.01f,settings.emissionDuration));
        effect.Tick(0);
        started=true;
    }

    public void Draw() { if(effect!=null) effect.DrawSnapshots(render.camera); }
    void DisposeEffect()
    {
        if(effect!=null) UnityEngine.Object.DestroyImmediate(effect.gameObject);
        effect=null; prefab=null; signature=null; simulatedTime=-1; started=false;
    }
    public void Dispose() => DisposeEffect();
}
