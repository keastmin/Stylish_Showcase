using UnityEngine;

namespace ProjectR.VectorJet
{
    public sealed class VectorJetPreview : MonoBehaviour
    {
        public VectorJetEffect[] jets;
        public float previewTime;
        public bool run=true;
        public float speed=1;
        public int solo=-1;
        public bool showPanel=true;
        GUIStyle title,small,button;
        void Update()
        {
            if(run) previewTime+=Time.deltaTime*speed;
            ApplyTime(previewTime);
        }
        public void ApplyTime(float time)
        {
            previewTime=time;
            if(jets==null) return;
            float t=Mathf.Repeat(time,4.8f);
            for(int i=0;i<jets.Length;i++)
            {
                if(jets[i]==null) continue;
                jets[i].graph.pause=!run;
                jets[i].graph.playRate=speed;
                float level=0;
                if(i==0) level=Envelope(t,0.45f,3.5f,0.055f,0.18f)*0.7f;
                if(i==1) level=Mathf.Max(Envelope(t,0.55f,0.84f,0.025f,0.09f),Mathf.Max(Envelope(t,1.5f,1.79f,0.025f,0.09f),Envelope(t,2.8f,3.09f,0.025f,0.09f)));
                if(i==2) level=Envelope(t,0.45f,2.45f,0.025f,0.16f);
                jets[i].SetThrottle(solo<0 || solo==i ? level : 0);
            }
        }
        static float Envelope(float t,float start,float end,float attack,float release)
        {
            if(t<start || t>end+release) return 0;
            return Mathf.SmoothStep(0,1,(t-start)/attack)*(1-Mathf.SmoothStep(0,1,(t-end)/release));
        }
        void OnGUI()
        {
            if(!showPanel) return;
            float scale=Mathf.Max(0.7f,Screen.width/1600f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            if(title==null)
            {
                title=new GUIStyle(GUI.skin.label){fontSize=26,fontStyle=FontStyle.Bold}; title.normal.textColor=new Color(0.77f,0.88f,1);
                small=new GUIStyle(GUI.skin.label){fontSize=13}; small.normal.textColor=new Color(0.46f,0.62f,0.77f);
                button=new GUIStyle(GUI.skin.button){fontSize=12};
            }
            float bottom=Screen.height/scale-64;
            if(GUI.Button(new Rect(40,bottom,90,28),run?"PAUSE":"PLAY",button)) run=!run;
            if(GUI.Button(new Rect(140,bottom,90,28),"RESTART",button)) {previewTime=0; foreach(var jet in jets) jet.graph.Reinit();}
            GUI.Label(new Rect(250,bottom,160,24),"PLAYBACK",small);
            speed=GUI.HorizontalSlider(new Rect(340,bottom+9,150,20),speed,0.15f,1.5f);
            GUI.Label(new Rect(510,bottom,180,24),speed.ToString("0.00")+"×  /  "+Mathf.Repeat(previewTime,4.8f).ToString("0.00")+" s",small);
            if(GUI.Button(new Rect(735,bottom,90,28),"ALL",button)) solo=-1;
            for(int i=0;i<3;i++) if(GUI.Button(new Rect(835+i*100,bottom,90,28),(i+1).ToString("00")+" SOLO",button)) solo=i;
        }
    }
}
