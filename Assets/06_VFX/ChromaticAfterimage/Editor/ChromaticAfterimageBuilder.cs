using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace ProjectR.VFX.Editor
{
    /// <summary>Authors only assets in this package. VFX Graph internals are resolved for the installed version.</summary>
    public static class ChromaticAfterimageBuilder
    {
        public const string Root="Assets/06_VFX/ChromaticAfterimage";
        public const string ScenePath=Root+"/Preview/ChromaticAfterimage_Preview.unity";
        public const string PrefabPath=Root+"/Prefabs/ChromaticAfterimage_VFX.prefab";
        public const string GraphPath=Root+"/Graphs/ChromaticWake.vfx";
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
        static PropertyInfo FindProperty(Type type,string name)
        {
            for(var t=type;t!=null;t=t.BaseType) { var p=t.GetProperty(name,Flags|BindingFlags.DeclaredOnly); if(p!=null) return p; }
            throw new MissingMemberException(type.FullName,name);
        }
        static object Get(object o,string name)
        {
            var f=o.GetType().GetField(name,Flags);
            if(f!=null) return f.GetValue(o);
            return FindProperty(o.GetType(),name).GetValue(o);
        }
        static void Property(object o,string name,object value)
        {
            var field=o.GetType().GetField(name,Flags);
            if(field!=null) field.SetValue(o,value);
            else FindProperty(o.GetType(),name).SetValue(o,value);
        }
        static object Call(object o,string name,params object[] args)
        {
            Type type=o as Type ?? o.GetType();
            var methods=type.GetMethods(Flags).Where(m=>m.Name==name && !m.IsGenericMethodDefinition);
            foreach(var m in methods)
            {
                var ps=m.GetParameters();
                if(ps.Length<args.Length || ps.Skip(args.Length).Any(p=>!p.IsOptional)) continue;
                if(args.Where((a,i)=>a!=null && !ps[i].ParameterType.IsInstanceOfType(a)).Any()) continue;
                var all=new object[ps.Length]; Array.Copy(args,all,args.Length);
                for(int i=args.Length;i<ps.Length;i++) all[i]=ps[i].DefaultValue;
                try { return m.Invoke(o is Type?null:o,all); }
                catch(TargetInvocationException e) { throw new InvalidOperationException(type.Name+"."+name+": "+e.InnerException?.Message,e.InnerException); }
            }
            throw new MissingMethodException(type.FullName,name+"("+string.Join(",",args.Select(a=>a?.GetType().Name))+")");
        }
        static object Make(string type) => ScriptableObject.CreateInstance(FindType("UnityEditor.VFX."+type));
        static void Add(object parent,object child) => Call(parent,"AddChild",child);
        static void Setting(object o,string key,object value)
        {
            var setting=Call(o,"GetSetting",key);
            var field=(FieldInfo)Get(setting,"field");
            if(field==null) throw new InvalidOperationException(o.GetType().Name+" has no setting "+key);
            Type t=field.FieldType;
            if(t.IsEnum && value is string) value=Enum.Parse(t,(string)value);
            else if(value!=null && !t.IsInstanceOfType(value)) value=Convert.ChangeType(value,t);
            Call(o,"SetSettingValue",key,value);
        }
        static object Slot(object o,string name) => ((IEnumerable)Get(o,"inputSlots")).Cast<object>().First(s=>(string)Get(s,"name")==name);
        static object Input(object o,int i) => Call(o,"GetInputSlot",i);
        static void WorldSpace(object slot)
        {
            var space=FindProperty(slot.GetType(),"space");
            space.SetValue(slot,Enum.Parse(space.PropertyType,"World"));
        }
        static void Value(object o,string name,object value) => Property(Slot(o,name),"value",value);
        static object Parameter(object graph,string name,Type type,object value,int order)
        {
            object p=Make("VFXParameter"); Call(p,"Init",type); Add(graph,p);
            Setting(p,"m_ExposedName",name); Setting(p,"m_Exposed",true);
            if(value!=null) Property(p,"value",value);
            Property(p,"position",new Vector2(-420,order*150));
            return p;
        }
        static void Link(object parameter,object slot) => Call(slot,"Link",Call(parameter,"GetOutputSlot",0));
        static object Node(object graph,string type,float x,float y)
        { var n=Make(type); Add(graph,n); Property(n,"position",new Vector2(x,y)); return n; }
        static object Block(object context,string type) { var b=Make("Block."+type); Add(context,b); return b; }
        static object Set(object context,string attribute,object min,object max=null)
        {
            var b=Block(context,"SetAttribute"); Setting(b,"attribute",attribute);
            if(max!=null) Setting(b,"Random",min is Vector3?"PerComponent":"Uniform");
            Property(Input(b,0),"value",min);
            if(max!=null) Property(Input(b,1),"value",max);
            return b;
        }
        static void Curve(object context,string attribute,AnimationCurve curve,string composition="Overwrite")
        {
            var b=Block(context,"AttributeFromCurve"); Setting(b,"attribute",attribute);
            Setting(b,"Mode","Uniform"); Setting(b,"Composition",composition);
            Property(Input(b,0),"value",curve);
        }
        static void DirectBlock(object context,string type,Action<object> configure=null)
        { var b=Make(type); Add(context,b); configure?.Invoke(b); }

        [MenuItem("Tools/ProjectR VFX/Build Chromatic Afterimage Assets")]
        public static void Build()
        {
            if(File.Exists(GraphPath))
            {
                Debug.Log("Chromatic Afterimage assets already exist. Use the preview window to inspect them.");
                Selection.activeObject=AssetDatabase.LoadMainAssetAtPath(PrefabPath);
                ChromaticAfterimageWindow.Open();
                return;
            }
            foreach(string folder in new[]{"Graphs","Materials","Textures","Prefabs","Preview"}) Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            var flake=Texture("WakeFlake",false); var star=Texture("WakeGlint",true);
            BuildGraph(flake,star);
            var ghost=Material(Root+"/Materials/ChromaticAfterimage.mat","ProjectR/VFX/Chromatic Afterimage");
            var floor=Material(Root+"/Materials/PreviewFloor.mat","ProjectR/VFX/Afterimage Preview Floor");
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject fx=new GameObject("Chromatic Afterimage VFX"); fx.layer=30;
                var effect=fx.AddComponent<ChromaticAfterimageTrail>(); effect.afterimageMaterial=ghost;
                var gpu=new GameObject("GPU Wake — VFX Graph"); gpu.transform.SetParent(fx.transform); gpu.layer=30;
                var visual=gpu.AddComponent<VisualEffect>(); visual.visualEffectAsset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GraphPath);
                visual.initialEventName="PreviewIdle"; visual.resetSeedOnPlay=false; visual.startSeed=142;
                effect.particles=visual;
                PrefabUtility.SaveAsPrefabAsset(fx,PrefabPath);
                Object.DestroyImmediate(fx);
                fx=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
                effect=fx.GetComponent<ChromaticAfterimageTrail>();
                var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/08_Prefabs/Combat/Player.prefab");
                var originalModel=player.GetComponentsInChildren<Animator>(true).First(a=>a.avatar!=null);
                var model=Object.Instantiate(originalModel.gameObject); model.name="Preview Player — original model and materials";
                foreach(var ps in model.GetComponentsInChildren<ParticleSystem>(true)) if(ps!=null) Object.DestroyImmediate(ps.gameObject);
                foreach(var b in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
                foreach(var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach(var a in model.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
                foreach(var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=30;
                model.transform.SetParent(fx.transform,false);
                var animator=model.GetComponent<Animator>(); animator.runtimeAnimatorController=null;
                var preview=fx.AddComponent<ChromaticAfterimagePreview>();
                preview.model=animator; preview.effect=effect;
                preview.dodgePose=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/06_Dodge/01_Dodge/Dodge_B_180.anim");
                if(preview.dodgePose==null)
                {
                    var ids=AssetDatabase.FindAssets("Dodge_B_180 t:AnimationClip");
                    preview.dodgePose=ids.Select(AssetDatabase.GUIDToAssetPath).SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(a=>!a.name.StartsWith("__"));
                }
                preview.skillPose=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/03_Animations/Player/Clips/Attack/Skill.anim");
                effect.sourceRoot=model.transform;
                preview.Seek(0);
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Preview Grid"; ground.layer=30;
                ground.transform.localScale=new Vector3(2,1,2); ground.transform.position=new Vector3(0,-0.018f,0);
                Object.DestroyImmediate(ground.GetComponent<Collider>()); ground.GetComponent<Renderer>().sharedMaterial=floor;
                var cameraGo=new GameObject("Afterimage Preview Camera"); cameraGo.layer=30;
                var camera=cameraGo.AddComponent<Camera>(); camera.cullingMask=1<<30; camera.depth=50;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.018f,0.027f,0.043f);
                camera.fieldOfView=34; camera.nearClipPlane=0.1f; camera.farClipPlane=80;
                camera.transform.position=new Vector3(3.7f,2.5f,-7.1f); camera.transform.LookAt(new Vector3(0,0.85f,0));
                camera.allowHDR=true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=1<<30;
                Light("Key Light",new Vector3(38,-38,0),new Color(0.83f,0.91f,1),2.0f);
                Light("Soft Blue Rim",new Vector3(28,145,0),new Color(0.25f,0.58f,1),1.4f);
                var volumeGo=new GameObject("Preview Bloom"); volumeGo.layer=30;
                var volume=volumeGo.AddComponent<Volume>(); volume.isGlobal=true;
                string profilePath=Root+"/Preview/PreviewBloom.asset";
                var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
                if(profile==null)
                {
                    profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,profilePath);
                    var bloom=profile.Add<Bloom>(); bloom.intensity.Override(0.38f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.55f);
                    AssetDatabase.AddObjectToAsset(bloom,profile);
                }
                volume.sharedProfile=profile;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
                Debug.Log("Chromatic Afterimage: independent VFX Graph, prefab and preview scene saved. Existing gameplay assets were not changed.");
            }
            finally
            {
                SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene,true);
            }
            AssetDatabase.SaveAssetIfDirty(ghost); AssetDatabase.SaveAssetIfDirty(floor);
        }
        static void Light(string name,Vector3 angles,Color color,float intensity)
        {
            var go=new GameObject(name); go.layer=30; go.transform.rotation=Quaternion.Euler(angles);
            var light=go.AddComponent<Light>(); light.type=LightType.Directional; light.color=color; light.intensity=intensity; light.cullingMask=1<<30; light.shadows=LightShadows.None;
        }
        static Material Material(string path,string shader)
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m,path); }
            return m;
        }
        static Texture2D Texture(string name,bool star)
        {
            string path=Root+"/Textures/"+name+".png";
            if(!File.Exists(path))
            {
                const int size=128; var t=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float u=(x+0.5f)/size*2-1,v=(y+0.5f)/size*2-1,r=u*u+v*v;
                    float alpha;
                    if(star) alpha=Mathf.Clamp01(Mathf.Exp(-r*34)+Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(u)),3)*Mathf.Exp(-v*v*1400)*0.75f+Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(v)),3)*Mathf.Exp(-u*u*1400)*0.75f);
                    else { float diamond=Mathf.Abs(u*0.92f+v*0.24f)+Mathf.Abs(v*1.35f-u*0.18f); alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.48f,0.72f,diamond)); }
                    pixels[y*size+x]=new Color(1,1,1,alpha);
                }
                t.SetPixels(pixels); t.Apply(); File.WriteAllBytes(path,t.EncodeToPNG()); Object.DestroyImmediate(t);
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.wrapMode=TextureWrapMode.Clamp; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void BuildGraph(Texture2D flake,Texture2D star)
        {
            if(File.Exists(GraphPath)) throw new InvalidOperationException("The standalone graph already exists. Keep the authored graph instead of rebuilding over edits.");
            string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(FindType("UnityEditor.VFX.VFXGraph").Assembly).resolvedPath;
            File.Copy(package+"/Editor/Templates/Empty.vfx",GraphPath);
            AssetDatabase.ImportAsset(GraphPath,ImportAssetOptions.ForceSynchronousImport);
            var resource=Call(FindType("UnityEditor.VFX.VisualEffectResource"),"GetResourceAtPath",GraphPath);
            var graph=Call(FindType("UnityEditor.VFX.VisualEffectResourceExtensions"),"GetOrCreateGraph",resource);
            ((Object)graph).name="Chromatic Wake — world-space surface fragments";
            var source=Parameter(graph,"SourcePose",typeof(Mesh),null,0);
            var center=Parameter(graph,"BoundsCenter",typeof(Vector3),Vector3.up,1);
            var rate=Parameter(graph,"FragmentRate",typeof(float),0f,2);
            var glints=Parameter(graph,"GlintRate",typeof(float),0f,3);
            System(graph,source,center,rate,flake,0,false);
            System(graph,source,center,glints,star,650,true);
            Call(graph,"UpdateSubAssets");
            Call(resource,"WriteAsset");
            AssetDatabase.ImportAsset(GraphPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        }
        static void System(object graph,object source,object center,object rate,Texture2D texture,float x,bool glints)
        {
            var spawn=Node(graph,"VFXBasicSpawner",x,0);
            var init=Node(graph,"VFXBasicInitialize",x,220);
            var update=Node(graph,"VFXBasicUpdate",x,830);
            var output=Node(graph,"VFXPlanarPrimitiveOutput",x,1180);
            Property(spawn,"label",glints?"02 · Icy glints":"01 · Blue surface fragments");
            Call(spawn,"LinkTo",init); Call(init,"LinkTo",update); Call(update,"LinkTo",output);
            Setting(init,"capacity",glints?256u:2048u);
            var data=Call(init,"GetData");
            var space=data.GetType().GetProperty("space",Flags); space.SetValue(data,Enum.Parse(space.PropertyType,"World"));
            Setting(data,"boundsMode","Manual");
            var bounds=Slot(init,"bounds");
            WorldSpace(bounds); // BoundsCenter is supplied in world coordinates by the trail.
            var children=((IEnumerable)Get(bounds,"children")).Cast<object>().ToArray();
            Link(center,children[0]); Property(children[1],"value",new Vector3(24,12,24));
            DirectBlock(spawn,"VFXSpawnerConstantRate",b=>Link(rate,Input(b,0)));
            var position=Block(init,"PositionMesh");
            Setting(position,"sourceMesh","Mesh"); Setting(position,"placementMode","Surface");
            Setting(position,"applyOrientation","None");
            Link(source,Slot(position,"mesh"));
            // SourcePose already contains world-space vertices. A Local transform
            // would apply the moving player's transform a second time.
            WorldSpace(Slot(position,"transform"));
            Set(init,"lifetime",glints?0.24f:0.45f,glints?0.6f:0.95f);
            Set(init,"size",glints?0.07f:0.024f,glints?0.13f:0.055f);
            Set(init,"color",new Vector3(0.04f,0.45f,1.5f),new Vector3(0.28f,1.2f,3.2f));
            Set(init,"velocity",new Vector3(-0.55f,0.12f,-0.55f),new Vector3(0.55f,1.0f,0.55f));
            if(!glints) { Set(init,"angleZ",0f,Mathf.PI*2); Set(init,"angularVelocityZ",-2.4f,2.4f); }
            var turbulence=Block(update,"Turbulence"); Setting(turbulence,"NoiseType","Perlin");
            Value(turbulence,"Intensity",glints?0.5f:1.1f); Value(turbulence,"frequency",1.8f);
            var drag=Block(update,"Drag"); Value(drag,"dragCoefficient",0.8f);
            Curve(output,"alpha",new AnimationCurve(new Keyframe(0,0),new Keyframe(0.07f,1),new Keyframe(0.35f,0.9f),new Keyframe(1,0)));
            Curve(output,"size",new AnimationCurve(new Keyframe(0,0.7f),new Keyframe(0.2f,1),new Keyframe(1,0.05f)),"Multiply");
            Block(output,"Orient");
            Setting(output,"blendMode","Additive"); Setting(output,"useBaseColorMap","ColorAndAlpha");
            Value(output,"mainTexture",texture);
        }

        public static void FinalizeAuthoredGraph()
        {
            var resource=Call(FindType("UnityEditor.VFX.VisualEffectResource"),"GetResourceAtPath",GraphPath);
            var graph=Call(FindType("UnityEditor.VFX.VisualEffectResourceExtensions"),"GetOrCreateGraph",resource);
            var nodes=((IEnumerable)Get(graph,"children")).Cast<object>().ToArray();
            var old=nodes.FirstOrDefault(n=>n.GetType().Name=="VFXParameter" && (string)Get(n,"exposedName")=="SourceSkin");
            var source=nodes.FirstOrDefault(n=>n.GetType().Name=="VFXParameter" && (string)Get(n,"exposedName")=="SourcePose") ?? Parameter(graph,"SourcePose",typeof(Mesh),null,0);
            int system=0;
            foreach(var init in nodes.Where(n=>n.GetType().Name=="VFXBasicInitialize"))
            {
                var data=Call(init,"GetData"); Property(data,"title",system==0?"Blue Surface Fragments":"Icy Glints");
                WorldSpace(Slot(init,"bounds"));
                foreach(var block in ((IEnumerable)Get(init,"children")).Cast<object>().ToArray())
                {
                    if(block.GetType().Name=="PositionMesh")
                    {
                        Setting(block,"sourceMesh","Mesh"); Setting(block,"skinnedTransform","None");
                        Link(source,Slot(block,"mesh"));
                        WorldSpace(Slot(block,"transform"));
                    }
                    if(block.GetType().Name!="SetAttribute") continue;
                    string attribute=(string)Get(block,"attribute");
                    if(attribute=="size") { Property(Input(block,0),"value",system==0?0.024f:0.07f); Property(Input(block,1),"value",system==0?0.055f:0.13f); }
                    if(attribute=="color") { Property(Input(block,0),"value",new Vector3(0.04f,0.45f,1.5f)); Property(Input(block,1),"value",new Vector3(0.28f,1.2f,3.2f)); }
                }
                system++;
            }
            if(old!=null) Call(graph,"RemoveChild",old);
            Call(graph,"UpdateSubAssets"); Call(resource,"WriteAsset");
            AssetDatabase.ImportAsset(GraphPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Repairs existing graphs without resetting authored colors, rates, sizes or curves.</summary>
        public static void RepairEmissionCoordinates()
        {
            var resource=Call(FindType("UnityEditor.VFX.VisualEffectResource"),"GetResourceAtPath",GraphPath);
            var graph=Call(FindType("UnityEditor.VFX.VisualEffectResourceExtensions"),"GetOrCreateGraph",resource);
            foreach(var init in ((IEnumerable)Get(graph,"children")).Cast<object>().Where(n=>n.GetType().Name=="VFXBasicInitialize"))
            {
                WorldSpace(Slot(init,"bounds"));
                foreach(var block in ((IEnumerable)Get(init,"children")).Cast<object>().Where(b=>b.GetType().Name=="PositionMesh"))
                    WorldSpace(Slot(block,"transform"));
            }
            Call(graph,"UpdateSubAssets"); Call(resource,"WriteAsset");
            AssetDatabase.ImportAsset(GraphPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        }
    }

    [InitializeOnLoad]
    public static class ChromaticAfterimageCapture
    {
        const string Key="ProjectR.ChromaticAfterimage.CaptureTime";
        static ChromaticAfterimageCapture() { EditorApplication.update+=Watch; }
        public static void Arm(float time) { SessionState.SetFloat(Key,time); }
        static void Watch()
        {
            float target=SessionState.GetFloat(Key,-1);
            if(target<0 || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            var preview=Object.FindFirstObjectByType<ChromaticAfterimagePreview>();
            if(preview==null || preview.previewTime<target) return;
            SessionState.EraseFloat(Key);
            EditorApplication.isPaused=true;
        }
    }
}
