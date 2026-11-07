using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectR.VFX.Editor
{
    public sealed class ChromaticAfterimageWindow : EditorWindow
    {
        ChromaticAfterimagePreview preview;
        [MenuItem("Tools/ProjectR VFX/Chromatic Afterimage Preview")]
        public static void Open() { var w=GetWindow<ChromaticAfterimageWindow>("Afterimage VFX"); w.minSize=new Vector2(340,430); }
        void OnEnable() { EditorApplication.update+=Tick; }
        void OnDisable() { EditorApplication.update-=Tick; }
        void Tick()
        {
            if(preview==null) preview=Object.FindFirstObjectByType<ChromaticAfterimagePreview>();
            if(Application.isPlaying) Repaint();
        }
        void OnGUI()
        {
            GUILayout.Space(12);
            GUILayout.Label("CHROMATIC AFTERIMAGE",EditorStyles.boldLabel);
            GUILayout.Label("Frozen color · dither dissolve · GPU blue wake",EditorStyles.miniLabel);
            GUILayout.Space(12);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if(GUILayout.Button("Open standalone preview scene",GUILayout.Height(30)))
            {
                var scene=SceneManager.GetSceneByPath(ChromaticAfterimageBuilder.ScenePath);
                if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(ChromaticAfterimageBuilder.ScenePath,OtherScenesDirty()?OpenSceneMode.Additive:OpenSceneMode.Single);
                preview=Object.FindFirstObjectByType<ChromaticAfterimagePreview>();
                var camera=FindCamera(scene);
                if(camera!=null && SceneView.lastActiveSceneView!=null) { SceneView.lastActiveSceneView.AlignViewToObject(camera.transform); SceneView.lastActiveSceneView.Repaint(); }
            }
            if(preview==null) { EditorGUILayout.HelpBox("Open the preview, then press Play here or use Unity Play mode. The original Player and skill effects are untouched.",MessageType.Info); return; }
            GUILayout.Space(8);
            EditorGUI.BeginChangeCheck(); bool skill=GUILayout.Toolbar(preview.skillMode?1:0,new[]{"PERFECT DODGE","E SKILL"})==1;
            if(EditorGUI.EndChangeCheck()) { preview.skillMode=skill; if(Application.isPlaying) Seek(0.64f); else preview.Seek(0); }
            GUILayout.Space(8);
            using(new EditorGUI.DisabledScope(OtherScenesDirty()))
            {
                string label=!Application.isPlaying?"Play preview (Unity Play Mode)":EditorApplication.isPaused?"Resume preview":"Pause preview";
                if(GUILayout.Button(label,GUILayout.Height(28)))
                {
                    if(!Application.isPlaying) { if(IsolatePreview()) { EditorApplication.isPaused=false; EditorApplication.isPlaying=true; } }
                    else EditorApplication.isPaused=!EditorApplication.isPaused;
                }
                if(Application.isPlaying && GUILayout.Button("Stop preview")) EditorApplication.isPlaying=false;
                using(new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    EditorGUI.BeginChangeCheck(); float time=EditorGUILayout.Slider("Time",preview.previewTime,0,preview.CycleDuration);
                    if(EditorGUI.EndChangeCheck()) Seek(time);
                }
                if(GUILayout.Button("Inspect strongest trail")) Seek(0.64f);
            }
            if(OtherScenesDirty()) EditorGUILayout.HelpBox("Save the other scene before starting this isolated preview. Your unsaved work is preserved.",MessageType.Info);
            else if(!Application.isPlaying) EditorGUILayout.HelpBox("Play Mode previews the frozen poses and GPU particles together. Time seeking replays the effect, then pauses it.",MessageType.Info);
            preview.playbackSpeed=EditorGUILayout.Slider("Playback speed",preview.playbackSpeed,0.1f,1.5f);
            GUILayout.Space(10);
            var e=preview.effect;
            e.snapshotLifetime=EditorGUILayout.Slider("Ghost lifetime",e.snapshotLifetime,0.2f,1.2f);
            e.snapshotInterval=EditorGUILayout.Slider("Pose spacing",e.snapshotInterval,0.035f,0.16f);
            e.opacity=EditorGUILayout.Slider("Ghost opacity",e.opacity,0.1f,0.95f);
            e.noiseScale=EditorGUILayout.Slider("Noise detail",e.noiseScale,8,60);
            e.fragmentsPerSecond=EditorGUILayout.Slider("Small particles / sec",e.fragmentsPerSecond,0,1500);
            e.glintsPerSecond=EditorGUILayout.Slider("Glints / sec",e.glintsPerSecond,0,220);
            GUILayout.Space(8);
            if(GUILayout.Button("Select VFX Graph")) Selection.activeObject=AssetDatabase.LoadMainAssetAtPath(ChromaticAfterimageBuilder.GraphPath);
            if(GUILayout.Button("Select standalone prefab")) Selection.activeObject=AssetDatabase.LoadMainAssetAtPath(ChromaticAfterimageBuilder.PrefabPath);
        }
        public void Seek(float time)
        {
            if(preview==null) preview=Object.FindFirstObjectByType<ChromaticAfterimagePreview>();
            if(preview==null) return;
            if(!Application.isPlaying && !IsolatePreview()) return;
            preview.Seek(0);
            ChromaticAfterimageCapture.Arm(time);
            EditorApplication.isPaused=false;
            if(!Application.isPlaying) EditorApplication.isPlaying=true;
        }
        static bool OtherScenesDirty()
        {
            for(int i=0;i<SceneManager.sceneCount;i++) { var s=SceneManager.GetSceneAt(i); if(s.path!=ChromaticAfterimageBuilder.ScenePath && s.isDirty) return true; }
            return false;
        }
        bool IsolatePreview()
        {
            if(OtherScenesDirty()) return false;
            var scene=preview.gameObject.scene;
            SceneManager.SetActiveScene(scene);
            for(int i=SceneManager.sceneCount-1;i>=0;i--) { var s=SceneManager.GetSceneAt(i); if(s!=scene) EditorSceneManager.CloseScene(s,true); }
            return true;
        }
        static Camera FindCamera(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects()) { var c=root.GetComponentInChildren<Camera>(); if(c!=null) return c; }
            return null;
        }
    }
}
