using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace ProjectR.UI.MainTitle.Editor
{
    public static class MainTitleCapture
    {
        [MenuItem("Tools/Project R/Main Title/Capture Layout Previews")]
        public static void CaptureAll()
        {
            Capture(1920,1080,"Main"); Capture(1280,720,"Main"); Capture(2560,1080,"Main"); Capture(1280,960,"Main");
            Capture(1920,1080,"Stages"); Capture(1920,1080,"Settings"); Capture(1920,1080,"Quit");
        }
        public static string Capture(int width,int height,string page)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Use Edit Mode layout capture.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(MainTitleBuilder.PrefabPath);
            var scene=EditorSceneManager.NewPreviewScene();var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;Texture2D pixels=null;
            try
            {
                var root=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);
                var c=root.GetComponent<MainTitleController>();c.enabled=false;
                foreach(var safe in root.GetComponentsInChildren<PlayerHUDSafeArea>(true)){safe.enabled=false;var r=(RectTransform)safe.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
                foreach(var feedback in root.GetComponentsInChildren<MainTitleFeedback>(true))feedback.enabled=false;
                c.introGroup.alpha=1;c.menuGroup.alpha=page=="Main"?1:.18f;
                c.stagesPanel.alpha=page=="Stages"?1:0;c.settingsPanel.alpha=page=="Settings"?1:0;c.quitPanel.alpha=page=="Quit"?1:0;
                var go=new GameObject("Title Preview Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(go,scene);var cam=go.GetComponent<Camera>();cam.scene=scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.cullingMask=1<<5;cam.orthographic=true;cam.orthographicSize=540;cam.nearClipPlane=.1f;cam.farClipPlane=50;cam.targetTexture=rt;cam.aspect=width/(float)height;
                var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=false;
                var canvas=root.GetComponent<Canvas>();root.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=1;canvas.scaleFactor=Mathf.Min(width/1920f,height/1080f);
                Canvas.ForceUpdateCanvases();foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))if(label.gameObject.activeInHierarchy)label.ForceMeshUpdate();Canvas.ForceUpdateCanvases();cam.Render();
                RenderTexture.active=rt;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                string path=Path.GetFullPath("output/MainTitle/"+page+"_"+width+"x"+height+".png");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
            }
            finally{RenderTexture.active=previous;if(pixels)Object.DestroyImmediate(pixels);rt.Release();Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
