using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace ProjectR.VectorJet.Editor
{
    public static class VectorJetCapture
    {
        public static void Freeze(float time)
        {
            var preview=Object.FindAnyObjectByType<VectorJetPreview>();
            if(preview==null) throw new System.InvalidOperationException("Open VectorJet_Preview first.");
            preview.run=false;
            preview.ApplyTime(time);
            foreach(var jet in preview.jets)
            {
                jet.graph.Reinit();
                jet.Apply();
                jet.graph.Simulate(1f/60,36);
                if(jet.throttle>0) jet.graph.SendEvent("Ignite");
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }
        public static string Capture(string name)
        {
            var go=GameObject.Find("VectorJet Preview Camera");
            var camera=go.GetComponent<Camera>();
            const int width=1920,height=1080;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active; var old=camera.targetTexture;
            try
            {
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
                string dir=Path.GetFullPath("output/VectorJet"); Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,name+".png"); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture); return path;
            }
            finally {camera.targetTexture=old; RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt);}
        }
    }
}
