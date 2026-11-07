using UnityEngine;
using UnityEngine.VFX;

namespace ProjectR.VectorJet
{
    [DisallowMultipleComponent]
    public sealed class VectorJetEffect : MonoBehaviour
    {
        public VisualEffect graph;
        public Light ignitionLight;
        [Range(0,1)] public float throttle;
        [Min(0.1f)] public float jetLength = 1.7f;
        [Min(0.1f)] public float jetWidth = 1;
        [ColorUsage(false,true)] public Color coreTint = new Color(.35f,1.2f,4.4f);
        [ColorUsage(false,true)] public Color shockTint = new Color(3.8f,2.6f,2);
        [ColorUsage(false,true)] public Color fringeTint = new Color(.18f,.55f,2.4f);
        public bool playOnEnable;
        float previous;
        float flash;
        float animationTime;
        static readonly int Throttle=Shader.PropertyToID("Throttle"), Length=Shader.PropertyToID("JetLength"), Width=Shader.PropertyToID("JetWidth");
        void OnEnable()
        {
            if(graph!=null) graph.Reinit();
            if(playOnEnable) Ignite();
            Apply();
        }
        public void Ignite()
        {
            flash=1;
            if(graph!=null) graph.SendEvent("Ignite");
        }
        public void SetThrottle(float value)
        {
            throttle=Mathf.Clamp01(value);
            if(previous<=0.03f && throttle>0.03f) Ignite();
            previous=throttle;
            Apply();
        }
        public void Cutoff() { SetThrottle(0); }
        void LateUpdate()
        {
            if(graph==null || !graph.pause) {flash=Mathf.MoveTowards(flash,0,Time.deltaTime*14); animationTime+=Time.deltaTime;}
            Apply();
        }
        public void Apply()
        {
            if(graph==null) return;
            float flutter=1+0.025f*Mathf.Sin(animationTime*67)+0.018f*Mathf.Sin(animationTime*111);
            if(graph.HasFloat(Throttle)) graph.SetFloat(Throttle,throttle);
            if(graph.HasFloat(Length)) graph.SetFloat(Length,jetLength*Mathf.Lerp(0.7f,1,throttle)*flutter);
            if(graph.HasFloat(Width)) graph.SetFloat(Width,jetWidth*(1+flash*0.16f));
            if(graph.HasVector3("CoreTint")) graph.SetVector3("CoreTint",new Vector3(coreTint.r,coreTint.g,coreTint.b));
            if(graph.HasVector3("ShockTint")) graph.SetVector3("ShockTint",new Vector3(shockTint.r,shockTint.g,shockTint.b));
            if(graph.HasVector3("FringeTint")) graph.SetVector3("FringeTint",new Vector3(fringeTint.r,fringeTint.g,fringeTint.b));
            if(ignitionLight!=null) ignitionLight.intensity=throttle*1.6f+flash*2;
        }
        void OnDisable() { throttle=0; previous=0; Apply(); if(graph!=null) graph.Stop(); if(ignitionLight!=null) ignitionLight.intensity=0; }
    }
}
