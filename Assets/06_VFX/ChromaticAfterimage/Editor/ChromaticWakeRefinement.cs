using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectR.VFX.Editor
{
    /// <summary>Explicit, repeatable art pass for the two GPU wake systems. Does not rebuild the graph or touch frozen poses.</summary>
    public static class ChromaticWakeRefinement
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const string Root = ChromaticAfterimageBuilder.Root;
        public const float FragmentRate = 720f, MoteRate = 90f;
        static object B(string method, params object[] args) => typeof(ChromaticAfterimageBuilder).GetMethod(method, Flags).Invoke(null,args);
        static object Get(object o,string key) => B("Get",o,key);
        static object[] Children(object o) => ((IEnumerable)Get(o,"children")).Cast<object>().ToArray();
        static void Setting(object o,string key,object v) => B("Setting",o,key,v);
        static void Property(object o,string key,object v) => B("Property",o,key,v);
        static void Value(object o,string key,object v) => B("Value",o,key,v);
        static object Input(object o,int i) => B("Input",o,i);
        static object Invoke(object o,string method,params object[] args) => B("Call",o,method,args);
        static void SlotValue(object slot,object value)
        {
            // Space-aware vector slots wrap Vector3 in UnityEditor.VFX.Vector.
            var current=Get(slot,"value");
            if(value is Vector3 && current!=null && current.GetType()!=typeof(Vector3))
            {
                var field=current.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(f=>f.FieldType==typeof(Vector3));
                if(field!=null) { field.SetValue(current,value); value=current; }
            }
            Property(slot,"value",value);
        }
        static void Range(object init,string attribute,object lo,object hi)
        {
            var block=Children(init).FirstOrDefault(x=>x.GetType().Name=="SetAttribute" && (string)Get(x,"attribute")==attribute);
            if(block==null) { B("Set",init,attribute,lo,hi); return; }
            Setting(block,"Random",lo is Vector3?"PerComponent":"Uniform");
            SlotValue(Input(block,0),lo); SlotValue(Input(block,1),hi);
        }
        static AnimationCurve Smooth(params float[] values)
        {
            var curve=new AnimationCurve();
            for(int i=0;i<values.Length;i+=2) curve.AddKey(new Keyframe(values[i],values[i+1]));
            // Zero tangents keep every segment monotonic and guarantee a soft zero-alpha endpoint.
            return curve;
        }
        static void Curve(object output,string attribute,AnimationCurve curve,string composition)
        {
            var block=Children(output).First(x=>x.GetType().Name=="AttributeFromCurve" && (string)Get(x,"attribute")==attribute);
            Setting(block,"Composition",composition); Setting(block,"SampleMode","OverLife");
            Property(Input(block,0),"value",curve);
        }

        [MenuItem("Tools/ProjectR VFX/Polish Original Small Wake Particles")]
        public static void Apply() => Configure(true);

        [MenuItem("Tools/ProjectR VFX/Restore Original Small Wake Style")]
        public static void RestoreOriginal() => Configure(false);

        static void Configure(bool polished)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply particle art tuning in Edit Mode.");
            var flake=polished?Texture("WakeSpeck",false):AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/WakeFlake.png");
            var glint=polished?Texture("WakeGlintSoft",true):AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/WakeGlint.png");
            var resource=Invoke(B("FindType","UnityEditor.VFX.VisualEffectResource"),"GetResourceAtPath",ChromaticAfterimageBuilder.GraphPath);
            var graph=Invoke(B("FindType","UnityEditor.VFX.VisualEffectResourceExtensions"),"GetOrCreateGraph",resource);
            var nodes=Children(graph);
            var init=nodes.Where(n=>n.GetType().Name=="VFXBasicInitialize").ToArray();
            var update=nodes.Where(n=>n.GetType().Name=="VFXBasicUpdate").ToArray();
            var output=nodes.Where(n=>n.GetType().Name=="VFXPlanarPrimitiveOutput").ToArray();
            var spawn=nodes.Where(n=>n.GetType().Name=="VFXBasicSpawner").ToArray();
            if(init.Length!=2 || update.Length!=2 || output.Length!=2 || spawn.Length!=2)
                throw new InvalidOperationException("Expected the two authored Chromatic Wake systems; no graph changes made.");
            for(int i=0;i<2;i++)
            {
                bool fine=i==1;
                Property(spawn[i],"label",fine?"02 · Icy glints":"01 · Blue surface specks");
                Property(Invoke(init[i],"GetData"),"title",fine?"Icy Glints":"Blue Surface Specks");
                Setting(init[i],"capacity",fine?256u:2048u);
                Range(init[i],"lifetime",fine?.24f:.45f,fine?.6f:.95f);
                Range(init[i],"size",fine?.07f:.024f,fine?.13f:.055f);
                Range(init[i],"color",new Vector3(.04f,.45f,1.5f),new Vector3(.28f,1.2f,3.2f));
                // The original author's raw Vector3 assignments were rejected by
                // space-aware velocity slots. Its visible motion came from curl noise.
                Range(init[i],"velocity",Vector3.zero,Vector3.zero);
                foreach(var block in Children(init[i]).Where(b=>b.GetType().Name=="SetAttribute"))
                {
                    var attribute=(string)Get(block,"attribute");
                    if(attribute=="alpha" || (fine && (attribute=="angleZ" || attribute=="angularVelocityZ")))
                        Invoke(init[i],"RemoveChild",block);
                }
                if(!fine) { Range(init[i],"angleZ",0f,Mathf.PI*2f); Range(init[i],"angularVelocityZ",-2.4f,2.4f); }
                var turbulence=Children(update[i]).First(b=>b.GetType().Name=="Turbulence");
                Setting(turbulence,"NoiseType","Perlin");
                Setting(turbulence,"Mode","Relative");
                Value(turbulence,"Intensity",fine?.5f:1.1f);
                Value(turbulence,"frequency",1.8f);
                Value(turbulence,"octaves",1);
                Value(turbulence,"roughness",.5f);
                Value(turbulence,"Drag",1f);
                var drag=Children(update[i]).First(b=>b.GetType().Name=="Drag");
                Value(drag,"dragCoefficient",.8f);
                Curve(output[i],"alpha",polished
                    ?Smooth(0,0,.09f,.94f,.35f,.86f,.72f,.28f,1,0)
                    :Smooth(0,0,.07f,1,.35f,.9f,1,0),"Overwrite");
                Curve(output[i],"size",Smooth(0,.7f,.2f,1,1,.05f),"Multiply");
                Setting(output[i],"blendMode","Additive");
                Setting(output[i],"useBaseColorMap","ColorAndAlpha");
                Setting(output[i],"useSoftParticle",false);
                Value(output[i],"mainTexture",fine?glint:flake);
            }
            ((Object)graph).name="Chromatic Wake — compact surface particles";
            Invoke(graph,"UpdateSubAssets"); Invoke(resource,"WriteAsset");
            AssetDatabase.ImportAsset(ChromaticAfterimageBuilder.GraphPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            ApplyRates();
            Debug.Log("Chromatic Wake: original compact particles at 720/90 per second; "+(polished?"soft faceted sprites and refined fade.":"original masks and fade."));
        }

        static void ApplyRates()
        {
            var prefab=PrefabUtility.LoadPrefabContents(ChromaticAfterimageBuilder.PrefabPath);
            try
            {
                var effect=prefab.GetComponent<ChromaticAfterimageTrail>();
                effect.fragmentsPerSecond=FragmentRate; effect.glintsPerSecond=MoteRate;
                PrefabUtility.SaveAsPrefabAsset(prefab,ChromaticAfterimageBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            // Gameplay applies this definition over the prefab every time it emits.
            var definition=AssetDatabase.LoadAssetAtPath<PlayerDodgeDefinition>("Assets/07_Datas/Player/PlayerDodge.asset");
            if(definition==null) throw new InvalidOperationException("Player dodge definition was not found.");
            Undo.RecordObject(definition,"Tune perfect dodge wake density");
            definition.afterimage.fragmentsPerSecond=FragmentRate;
            definition.afterimage.glintsPerSecond=MoteRate;
            EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
        }

        static Texture2D Texture(string name,bool glint)
        {
            const int n=256;
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,false,true);
            var pixels=new Color[n*n];
            for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                float u=(x+.5f)/n*2-1,v=(y+.5f)/n*2-1;
                float r=u*u+v*v;
                float alpha, luminance=1f;
                if(!glint)
                {
                    // Compact diamond body, soft bevel and a small halo; no elongated stroke.
                    float diamond=Mathf.Abs(u*.92f+v*.24f)+Mathf.Abs(v*1.35f-u*.18f);
                    // Mathf.SmoothStep takes (from, to, t), not shader-style edge thresholds.
                    // Normalize first so the outside of the sprite is fully transparent.
                    float body=1-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.28f,.66f,diamond));
                    float core=Mathf.Exp(-r*56f);
                    float halo=Mathf.Exp(-r*11f)*Mathf.Clamp01(1-r);
                    alpha=Mathf.Clamp01(body*.80f+core*.12f+halo*.08f);
                    luminance=.83f+.17f*Mathf.Clamp01(.5f+(u-v)*.9f);
                }
                else
                {
                    float core=Mathf.Exp(-r*44f);
                    float horizontal=Mathf.Exp(-v*v*600f)*Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(u)/.75f),3);
                    float vertical=Mathf.Exp(-u*u*600f)*Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(v)/.75f),3);
                    alpha=Mathf.Clamp01(core*.72f+(horizontal+vertical)*.3f+Mathf.Exp(-r*12f)*.08f);
                }
                pixels[y*n+x]=new Color(luminance,luminance,luminance,Mathf.Clamp01(alpha));
            }
            string path=Root+"/Textures/"+name+".png";
            try { texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=true; importer.sRGBTexture=false;
            importer.mipmapEnabled=true; importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Trilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
