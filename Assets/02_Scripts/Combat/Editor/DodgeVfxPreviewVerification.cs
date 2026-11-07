using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Checks actual GPU rendering inside Dodge Studio in Edit Mode, using temporary tuning.</summary>
public static class DodgeVfxPreviewVerification
{
    const string With="Temp/DodgeVfxInline_With.png", Without="Temp/DodgeVfxInline_Without.png", Held="Temp/DodgeVfxInline_Held.png";
    static PlayerDodgeStudio window;
    static PlayerDodgeDefinition original,test;
    static string authored;
    static bool wasDirty;
    static double start;
    static int phase,checks,visiblePixels,waits;
    static Renderer particles;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;

    [MenuItem("Tools/Combat/Validate Dodge Studio GPU Preview")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run this check in Edit Mode.");
        window=EditorWindow.GetWindow<PlayerDodgeStudio>();
        original=(PlayerDodgeDefinition)typeof(PlayerDodgeStudio).GetField("_definition",Flags).GetValue(window);
        if(original==null) original=AssetDatabase.LoadAssetAtPath<PlayerDodgeDefinition>(PlayerDodgeSetup.DefinitionPath);
        authored=EditorJsonUtility.ToJson(original); wasDirty=EditorUtility.IsDirty(original);
        test=Object.Instantiate(original); test.hideFlags=HideFlags.HideAndDontSave;
        test.afterimage.mode=DodgeAfterimageMode.Chromatic;
        test.afterimage.prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
        test.afterimage.opacity=0; test.afterimage.useRealTime=true;
        test.afterimage.emissionDuration=.8f; test.afterimage.fragmentsPerSecond=900;
        test.afterimage.glintsPerSecond=120; test.afterimage.start=DodgeAfterimageStart.DodgeStart;
        test.closeAtAnimationMarker=false; test.slow.SlowScale=1; test.dodgeEffects.Clear(); test.counterEffects.Clear();
        PlayerDodgeStudio.OpenDefinition(test);
        window.PreviewAfterimage(); window.PausePreview(); window.PreparePreview();
        typeof(PlayerDodgeStudio).GetMethod("Scrub",Flags).Invoke(window,new object[]{.4f});
        phase=0; checks=0; waits=0; visiblePixels=0; start=EditorApplication.timeSinceStartup;
        EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        window.Focus(); window.Repaint();
    }

    static void Check(bool condition,string reason) { if(!condition) throw new InvalidOperationException(reason); checks++; }
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-start>30) throw new TimeoutException("GPU preview verification timed out.");
            if(phase==0)
            {
                if(window.PreviewVfxCatchingUp || window.PreviewParticleCount<=0) { window.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); return; }
                Check(!Application.isPlaying,"GPU preview runs without Play Mode.");
                Check(window.PreviewAfterimageCount>0,"The tool produces frozen mesh poses.");
                Check(window.PreviewParticleCount>0,"The actual VFX Graph reports live GPU particles.");
                window.CapturePreview(With); phase=1; return;
            }
            if(phase==1)
            {
                if(!File.Exists(With) || PendingCapture()) return;
                window.CapturePreview(Held); phase=2; return;
            }
            if(phase==2)
            {
                if(!File.Exists(Held) || PendingCapture()) return;
                Check(Difference(With,Held)==0,"Pause preserves the same rendered GPU particle image.");
                var preview=(DodgeStudioPreview)typeof(PlayerDodgeStudio).GetField("_preview",Flags).GetValue(window);
                particles=preview.Afterimage.Effect.particles.GetComponent<Renderer>();
                particles.forceRenderingOff=true;
                window.CapturePreview(Without); phase=3; return;
            }
            if(phase==3)
            {
                if(!File.Exists(Without) || PendingCapture()) return;
                int changed=Difference(With,Without);
                Check(changed>40,"Turning off only the GPU renderer changes visible pixels ("+changed+").");
                visiblePixels=changed;
                particles.forceRenderingOff=false;
                typeof(PlayerDodgeStudio).GetMethod("Scrub",Flags).Invoke(window,new object[]{.15f});
                phase=4; return;
            }
            if(phase==4)
            {
                if(window.PreviewVfxCatchingUp || window.PreviewParticleCount<=0) { window.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); return; }
                var preview=(DodgeStudioPreview)typeof(PlayerDodgeStudio).GetField("_preview",Flags).GetValue(window);
                Check(Mathf.Abs(preview.Afterimage.SimulatedTime-.15f)<.0001f,"Backward scrubbing rebuilds particles at the requested time.");
                test.afterimage.fragmentsPerSecond=0; test.afterimage.glintsPerSecond=0;
                window.Repaint(); phase=5; waits=0; return;
            }
            if(phase==5)
            {
                if(++waits<4 || window.PreviewVfxCatchingUp || window.PreviewParticleCount>0)
                { window.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); return; }
                Check(window.PreviewAfterimageCount>0 && window.PreviewParticleCount==0,"Changing both particle rates to zero updates this preview immediately.");
                Check(authored==EditorJsonUtility.ToJson(original) && wasDirty==EditorUtility.IsDirty(original),"User's unsaved settings are preserved.");
                Finish(null,visiblePixels);
            }
        }
        catch(Exception error) { Finish(error,0); }
    }
    static bool PendingCapture() => typeof(PlayerDodgeStudio).GetField("_capturePath",Flags).GetValue(window)!=null;
    static int Difference(string a,string b)
    {
        var first=new Texture2D(2,2); var second=new Texture2D(2,2);
        try
        {
            first.LoadImage(File.ReadAllBytes(a)); second.LoadImage(File.ReadAllBytes(b));
            if(first.width!=second.width || first.height!=second.height) return int.MaxValue;
            var x=first.GetPixels32(); var y=second.GetPixels32(); int changes=0;
            for(int i=0;i<x.Length;i++) if(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b)>8) changes++;
            return changes;
        }
        finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
    }
    static void Finish(Exception error,int pixels)
    {
        EditorApplication.update-=Tick;
        if(particles!=null) particles.forceRenderingOff=false;
        File.WriteAllText("Temp/DodgeVfxPreviewVerification.result",error==null?"PASS "+checks+" checks; "+pixels+" visible GPU pixels\n":"FAIL "+checks+" checks\n"+error);
        PlayerDodgeStudio.OpenDefinition(original); window.PreviewAfterimage();
        if(test!=null) Object.DestroyImmediate(test); test=null;
        if(error!=null) Debug.LogException(error); else Debug.Log("Dodge Studio GPU preview: "+checks+" checks passed; "+pixels+" pixels verified.");
    }
}
