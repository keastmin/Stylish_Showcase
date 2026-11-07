using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object=UnityEngine.Object;

namespace ProjectR.VectorJet.Editor
{
    public static class VectorJetStudioBuilder
    {
        public const string Root="Assets/06_VFX/VectorJet";
        public const string ScenePath=Root+"/Preview/VectorJet_Preview.unity";
        public const string PrefabPath=Root+"/Prefabs/VectorJet_Thruster.prefab";
        const int Layer=29;
        [MenuItem("Tools/ProjectR/Vector Jet/Open Preview Scene")]
        public static void OpenPreview()
        {
            if(EditorApplication.isPlaying) return;
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        public static void Build()
        {
            foreach(var folder in new[]{"Graphs","Materials","Prefabs","Preview","Documentation"}) Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            string texPath=Root+"/Textures/JetFlameMask.png";
            var ti=(TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.sRGBTexture=false; ti.alphaSource=TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency=false; ti.wrapMode=TextureWrapMode.Clamp; ti.mipmapEnabled=true; ti.textureCompression=TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
            foreach(var filename in new[]{"JetCore","JetShockCells","JetNozzle"})
            {
                var mi=(ModelImporter)AssetImporter.GetAtPath(Root+"/Models/"+filename+".fbx");
                mi.isReadable=true; mi.importCameras=false; mi.importLights=false; mi.materialImportMode=ModelImporterMaterialImportMode.ImportStandard; mi.SaveAndReimport();
            }
            var core=AssetDatabase.LoadAllAssetsAtPath(Root+"/Models/JetCore.fbx").OfType<Mesh>().First();
            var shock=AssetDatabase.LoadAllAssetsAtPath(Root+"/Models/JetShockCells.fbx").OfType<Mesh>().First();
            VectorJetGraphBuilder.Build(AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),core,shock);
            var body=MakeMaterial("NozzleGraphite",new Color(0.07f,0.09f,0.13f),0.8f,0.63f);
            var metal=MakeMaterial("NozzleTitanium",new Color(0.34f,0.4f,0.47f),0.85f,0.58f);
            var ceramic=MakeMaterial("NozzleCeramic",new Color(0.2f,0.23f,0.28f),0.35f,0.35f);
            var warm=MakeMaterial("NozzleHeat",new Color(0.15f,0.07f,0.025f),0.65f,0.5f);
            warm.EnableKeyword("_EMISSION"); warm.SetColor("_EmissionColor",new Color(0.9f,0.18f,0.025f)*0.8f);
            var accent=MakeMaterial("NozzleIndicator",new Color(0.03f,0.2f,0.32f),0.6f,0.7f);
            accent.EnableKeyword("_EMISSION"); accent.SetColor("_EmissionColor",new Color(0.04f,0.65f,1.1f));
            var floor=MakeMaterial("StudioFloor",new Color(0.022f,0.032f,0.049f),0.5f,0.55f);
            foreach(var m in new[]{body,metal,ceramic,warm,accent,floor}) AssetDatabase.SaveAssetIfDirty(m);
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var fx=new GameObject("VectorJet Thruster");
                var effect=fx.AddComponent<VectorJetEffect>();
                var gpu=new GameObject("VectorJet GPU Graph"); gpu.transform.SetParent(fx.transform,false);
                var vfx=gpu.AddComponent<VisualEffect>(); vfx.visualEffectAsset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(VectorJetGraphBuilder.GraphPath); vfx.startSeed=927; vfx.resetSeedOnPlay=false; effect.graph=vfx;
                var glow=new GameObject("Ignition Light"); glow.transform.SetParent(fx.transform,false); glow.transform.localPosition=Vector3.forward*0.1f;
                var light=glow.AddComponent<Light>(); light.type=LightType.Point; light.color=new Color(0.22f,0.48f,1); light.range=2; light.intensity=0; light.shadows=LightShadows.None; effect.ignitionLight=light;
                effect.throttle=0; effect.Apply();
                PrefabUtility.SaveAsPrefabAsset(fx,PrefabPath); Object.DestroyImmediate(fx);
                var preview=new GameObject("VectorJet Preview Controls").AddComponent<VectorJetPreview>(); preview.jets=new VectorJetEffect[3];
                Label("VECTOR / JET",new Vector3(-1.85f,3.64f,0),0.085f,new Color(0.72f,0.85f,1));
                var nozzle=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/JetNozzle.fbx");
                string[] labels={"01  /  HOVER HOLD","02  /  VECTOR IMPULSE","03  /  FULL IGNITION"};
                for(int i=0;i<3;i++)
                {
                    var station=new GameObject(labels[i]); station.transform.position=new Vector3(-1.25f,2.7f-i*1.05f,0); station.transform.rotation=Quaternion.Euler(0,90,0);
                    var housing=(GameObject)PrefabUtility.InstantiatePrefab(nozzle,scene); housing.name="Blender Nozzle"; housing.transform.SetParent(station.transform,false);
                    foreach(var renderer in housing.GetComponentsInChildren<Renderer>())
                    {
                        renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>
                        { string n=m==null?"":m.name.ToLowerInvariant(); return n.Contains("heat")||n.Contains("copper")?warm:n.Contains("accent")||n.Contains("emiss")||n.Contains("indicator")?accent:n.Contains("ceramic")?ceramic:n.Contains("steel")||n.Contains("titan")||n.Contains("metal")?metal:body; }).ToArray();
                    }
                    fx=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene); fx.transform.SetParent(station.transform,false);
                    effect=fx.GetComponent<VectorJetEffect>(); effect.jetLength=i==0?1.6f:i==1?1.25f:2.05f; effect.jetWidth=i==0?0.92f:1.0f;
                    preview.jets[i]=effect;
                    if(i==0)
                    {
                        var position=station.transform.position; var rotation=station.transform.rotation;
                        station.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                        PrefabUtility.SaveAsPrefabAsset(station,Root+"/Prefabs/VectorJet_NozzlePreview.prefab");
                        station.transform.SetPositionAndRotation(position,rotation);
                    }
                    Label(labels[i],new Vector3(-1.85f,3.12f-i*1.05f,-0.03f),0.047f,new Color(0.48f,0.66f,0.84f));
                }
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Studio Floor"; ground.transform.localScale=new Vector3(3,1,3); ground.transform.position=new Vector3(0,-0.05f,0); Object.DestroyImmediate(ground.GetComponent<Collider>()); ground.GetComponent<Renderer>().sharedMaterial=floor;
                var cameraGo=new GameObject("VectorJet Preview Camera"); var camera=cameraGo.AddComponent<Camera>();
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.035f,0.05f,0.08f); camera.cullingMask=1<<Layer; camera.depth=60; camera.allowHDR=true;
                camera.orthographic=true; camera.orthographicSize=2.15f; camera.nearClipPlane=0.1f; camera.farClipPlane=40;
                camera.transform.position=new Vector3(1.7f,2.65f,-7.5f); camera.transform.LookAt(new Vector3(-0.2f,1.8f,0));
                var additional=camera.GetUniversalAdditionalCameraData(); additional.renderPostProcessing=true; additional.requiresDepthTexture=true; additional.volumeLayerMask=1<<Layer; additional.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                MakeLight("Studio Key",new Vector3(25,10,0),new Color(0.9f,0.93f,1),4);
                MakeLight("Studio Rim",new Vector3(25,145,0),new Color(0.22f,0.42f,1),2.1f);
                var vg=new GameObject("VectorJet Bloom"); var volume=vg.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=100;
                var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/Preview/VectorJetBloom.asset");
                if(profile==null) {profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,Root+"/Preview/VectorJetBloom.asset");}
                if(!profile.TryGet<Bloom>(out var bloom)) {bloom=profile.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bloom,profile);}
                bloom.intensity.Override(0.4f); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.58f);
                volume.sharedProfile=profile; AssetDatabase.SaveAssetIfDirty(profile);
                foreach(var root in scene.GetRootGameObjects()) foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=Layer;
                foreach(var jet in preview.jets) jet.ignitionLight.cullingMask=1<<Layer;
                ground.SetActive(false);
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally {SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene,true);}
            Debug.Log("VectorJet saved: native VFX Graph, reusable prefab, Blender meshes and independent preview scene.");
        }
        static void Label(string text,Vector3 pos,float scale,Color color)
        {
            var go=new GameObject(text); go.transform.position=pos; var t=go.AddComponent<TextMesh>(); t.text=text; t.fontSize=64; t.characterSize=scale*0.26f; t.color=color; t.anchor=TextAnchor.MiddleLeft;
        }
        static Material MakeMaterial(string name,Color color,float metallic,float smooth)
        {
            string path=Root+"/Materials/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) {m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color); m.SetFloat("_Metallic",metallic); m.SetFloat("_Smoothness",smooth); return m;
        }
        static void MakeLight(string name,Vector3 angle,Color color,float intensity)
        {
            var go=new GameObject(name); go.transform.rotation=Quaternion.Euler(angle); var l=go.AddComponent<Light>(); l.type=LightType.Directional; l.color=color; l.intensity=intensity; l.shadows=LightShadows.None; l.cullingMask=1<<Layer;
        }
    }
}
