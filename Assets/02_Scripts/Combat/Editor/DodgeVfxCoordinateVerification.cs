using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Renders the real GPU wake around a world-baked player pose under different emitter transforms.</summary>
public static class DodgeVfxCoordinateVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static PlayerDodgeStudio window;
    static PlayerDodgeDefinition original, test;
    static DodgeStudioPreview preview;
    static Transform holder, emitter;
    static Renderer renderer;
    static IList frames;
    static Vector3[] positions;
    static Vector3 holderScale, emitterPosition, emitterScale;
    static Quaternion holderRotation, emitterRotation;
    static string authored, prefix;
    static bool dirty, observeOnly, testThreat, transformsCaptured;
    static int scenario, phase, waits;
    static double started;
    static StringBuilder result;

    public static void RunBaseline() => Begin(true);

    [MenuItem("Tools/Combat/Validate Dodge VFX World Coordinates")]
    public static void Run() => Begin(false);

    static void Begin(bool baseline)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        observeOnly=baseline; scenario=0; phase=0; waits=0; transformsCaptured=false;
        result=new StringBuilder();
        prefix="Temp/DodgeVfxCoordinates_"+(baseline?"Before":"After");
        window=EditorWindow.GetWindow<PlayerDodgeStudio>();
        testThreat=(bool)typeof(PlayerDodgeStudio).GetField("_testThreat",Flags).GetValue(window);
        typeof(PlayerDodgeStudio).GetField("_testThreat",Flags).SetValue(window,false);
        original=(PlayerDodgeDefinition)typeof(PlayerDodgeStudio).GetField("_definition",Flags).GetValue(window);
        if(original==null) original=AssetDatabase.LoadAssetAtPath<PlayerDodgeDefinition>(PlayerDodgeSetup.DefinitionPath);
        authored=EditorJsonUtility.ToJson(original); dirty=EditorUtility.IsDirty(original);
        test=Object.Instantiate(original); test.hideFlags=HideFlags.HideAndDontSave;
        test.afterimage.mode=DodgeAfterimageMode.Chromatic;
        test.afterimage.prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
        test.afterimage.opacity=0; test.afterimage.useRealTime=true; test.afterimage.stopOnDodgeExit=false;
        test.afterimage.emissionDuration=.8f; test.afterimage.fragmentsPerSecond=900; test.afterimage.glintsPerSecond=120;
        test.afterimage.start=DodgeAfterimageStart.PerfectSuccess;
        test.closeAtAnimationMarker=false; test.slow.SlowScale=1; test.dodgeEffects.Clear(); test.counterEffects.Clear();
        PlayerDodgeStudio.OpenDefinition(test);
        window.PreviewAfterimage(); window.PausePreview(); window.PreparePreview();
        preview=(DodgeStudioPreview)typeof(PlayerDodgeStudio).GetField("_preview",Flags).GetValue(window);
        holder=((GameObject)typeof(DodgeStudioPreview).GetField("_holder",Flags).GetValue(preview)).transform;
        phase=-1; started=EditorApplication.timeSinceStartup;
        EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        window.Focus(); window.Repaint();
    }

    static void InitializeScenarios()
    {
        emitter=preview.Afterimage.Effect.transform;
        renderer=preview.Afterimage.Effect.particles.GetComponent<Renderer>();
        holderScale=holder.localScale; holderRotation=holder.rotation;
        emitterPosition=emitter.position; emitterRotation=emitter.rotation; emitterScale=emitter.localScale;
        transformsCaptured=true;
        frames=(IList)typeof(PlayerDodgeStudio).GetField("_frames",Flags).GetValue(window);
        positions=new Vector3[frames.Count];
        for(int i=0;i<frames.Count;i++) positions[i]=(Vector3)frames[i].GetType().GetField("position",Flags).GetValue(frames[i]);
        SetScenario();
    }

    static void SetScenario()
    {
        Vector3 translation=scenario==0?Vector3.zero:new Vector3(83.5f,12.4f,-117.8f);
        Quaternion rotation=scenario<2?Quaternion.identity:Quaternion.Euler(0,80,0);
        Vector3 scale=scenario<3?Vector3.one:new Vector3(1.4f,.8f,1.7f);
        var matrix=Matrix4x4.TRS(translation,rotation,scale);
        for(int i=0;i<frames.Count;i++)
        {
            object frame=frames[i];
            frame.GetType().GetField("position",Flags).SetValue(frame,matrix.MultiplyPoint3x4(positions[i]));
            frames[i]=frame;
        }
        holder.rotation=rotation; holder.localScale=Vector3.Scale(holderScale,scale);
        emitter.SetPositionAndRotation(translation,rotation); emitter.localScale=scale;
        renderer.forceRenderingOff=false;
        preview.Afterimage.Reset();
        typeof(PlayerDodgeStudio).GetMethod("Scrub",Flags).Invoke(window,new object[]{.4f});
        started=EditorApplication.timeSinceStartup; waits=0; phase=0;
    }

    static string PathFor(string suffix) => prefix+"_"+scenario+"_"+suffix+".png";
    static bool PendingCapture() => typeof(PlayerDodgeStudio).GetField("_capturePath",Flags).GetValue(window)!=null;
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-started>30) throw new TimeoutException("Coordinate rendering check timed out at scenario "+scenario);
            if(phase==-1)
            {
                window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                if(preview.Afterimage.Effect!=null) InitializeScenarios();
                return;
            }
            if(phase==0)
            {
                window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                if(window.PreviewVfxCatchingUp || ++waits<8) return;
                window.CapturePreview(PathFor("With")); phase=1; return;
            }
            if(phase==1)
            {
                if(PendingCapture() || !File.Exists(PathFor("With"))) return;
                renderer.forceRenderingOff=true;
                window.CapturePreview(PathFor("Without")); phase=2; return;
            }
            if(PendingCapture() || !File.Exists(PathFor("Without"))) return;
            int pixels=Difference(PathFor("With"),PathFor("Without"));
            result.AppendLine("Scenario "+scenario+": "+pixels+" visible GPU pixels; "+window.PreviewParticleCount+" live particles.");
            if(!observeOnly && pixels<=40) throw new InvalidOperationException("GPU particles must render beside the transformed player; scenario "+scenario);
            if(++scenario<4) { SetScenario(); return; }
            if(authored!=EditorJsonUtility.ToJson(original) || dirty!=EditorUtility.IsDirty(original)) throw new InvalidOperationException("Authored dodge settings changed.");
            Finish(null);
        }
        catch(Exception e) { Finish(e); }
    }

    static int Difference(string a,string b)
    {
        var first=new Texture2D(2,2); var second=new Texture2D(2,2);
        try
        {
            first.LoadImage(File.ReadAllBytes(a)); second.LoadImage(File.ReadAllBytes(b));
            if(first.width!=second.width || first.height!=second.height) throw new InvalidOperationException("Preview size changed during capture.");
            var x=first.GetPixels32(); var y=second.GetPixels32(); int changes=0;
            for(int i=0;i<x.Length;i++) if(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b)>8) changes++;
            return changes;
        }
        finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
    }

    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;
        if(renderer!=null) renderer.forceRenderingOff=false;
        if(transformsCaptured && holder!=null) { holder.rotation=holderRotation; holder.localScale=holderScale; }
        if(transformsCaptured && emitter!=null) { emitter.SetPositionAndRotation(emitterPosition,emitterRotation); emitter.localScale=emitterScale; }
        result.Insert(0,error==null?(observeOnly?"BASELINE\n":"PASS\n"):"FAIL\n");
        if(error!=null) result.AppendLine(error.ToString());
        File.WriteAllText(prefix+".result",result.ToString());
        typeof(PlayerDodgeStudio).GetField("_testThreat",Flags).SetValue(window,testThreat);
        PlayerDodgeStudio.OpenDefinition(original); window.PreviewAfterimage();
        if(test!=null) Object.DestroyImmediate(test); test=null;
        if(error!=null) Debug.LogException(error); else Debug.Log(result.ToString());
    }
}
