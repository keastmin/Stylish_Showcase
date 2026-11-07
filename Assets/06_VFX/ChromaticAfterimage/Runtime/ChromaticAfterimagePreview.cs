using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ProjectR.VFX
{
    /// <summary>A self-contained model/animation showcase. Never drives the real Player.</summary>
    [ExecuteAlways]
    public sealed class ChromaticAfterimagePreview : MonoBehaviour
    {
        public ChromaticAfterimageTrail effect;
        public Animator model;
        public AnimationClip dodgePose;
        public AnimationClip skillPose;
        public bool autoplay=true;
        public bool skillMode;
        [Range(0.1f,1.5f)] public float playbackSpeed=0.65f;
        public float distance=4.2f;
        [Min(.01f)] public float emissionDuration=.34f;
        [HideInInspector] public float previewTime;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        bool started;
        bool boundSkillMode;
        AnimationClip boundClip;
        const float Cycle=2.6f;
        public float CycleDuration=>Mathf.Max(Cycle,.32f+emissionDuration+(effect!=null?effect.snapshotLifetime:.64f)+1);
        AnimationClip ActiveClip=>skillMode && skillPose!=null?skillPose:dodgePose;

        void OnEnable() { CreateGraph(); }
        void OnDisable() { if(graph.IsValid()) graph.Destroy(); }
        void CreateGraph()
        {
            if(graph.IsValid()) graph.Destroy();
            if(model==null || dodgePose==null) return;
            model.applyRootMotion=false; model.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            model.fireEvents=false;
            graph=PlayableGraph.Create("Chromatic Afterimage Preview"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output=AnimationPlayableOutput.Create(graph,"Pose",model);
            boundSkillMode=skillMode;
            boundClip=ActiveClip;
            playable=AnimationClipPlayable.Create(graph,boundClip);
            playable.SetApplyFootIK(false); output.SetSourcePlayable(playable); graph.Play();
            if(effect!=null)
            {
                effect.externalClock=true; effect.Bind(model.transform);
                if(effect.particles!=null) effect.particles.pause=!Application.isPlaying;
            }
            ResetPreview();
        }
        void Update() { if(Application.isPlaying && autoplay) Step(Mathf.Min(Time.unscaledDeltaTime,1f/30f)*playbackSpeed,false); }
        public void ResetPreview()
        {
            previewTime=0; started=false;
            if(effect!=null) effect.Clear();
            EvaluatePose();
        }
        public void Seek(float time)
        {
            if(!graph.IsValid() || boundSkillMode!=skillMode || boundClip!=ActiveClip) CreateGraph();
            ResetPreview();
            float end=Mathf.Clamp(time,0,CycleDuration);
            while(previewTime+0.0001f<end) Step(Mathf.Min(1f/90f,end-previewTime),true);
        }
        public void Step(float dt,bool manualParticles)
        {
            if(!graph.IsValid() || boundSkillMode!=skillMode || boundClip!=ActiveClip) CreateGraph();
            if(effect==null || model==null) return;
            if(previewTime+dt>=CycleDuration) ResetPreview();
            previewTime+=dt;
            EvaluatePose();
            if(previewTime>=0.32f && !started) { effect.Play(Mathf.Max(.01f,emissionDuration)); started=true; }
            effect.Tick(dt);
            if(manualParticles && effect.particles!=null) effect.particles.Simulate(dt,1);
        }
        void EvaluatePose()
        {
            if(model==null || !graph.IsValid()) return;
            float u=Mathf.Clamp01((previewTime-0.32f)/Mathf.Max(.01f,emissionDuration));
            float move=u*u*(3-2*u);
            model.transform.localPosition=new Vector3(Mathf.Lerp(-distance*0.5f,distance*0.5f,move),0,0);
            model.transform.localRotation=Quaternion.Euler(0,-38,0);
            // The original avatar is retargeted through AnimationClipPlayable; no copied skeleton animation.
            float clipTime=skillMode?0.3f+u*0.4f:0.12f+u*0.33f;
            playable.SetTime(clipTime); playable.SetSpeed(0); graph.Evaluate(0);
        }
    }
}
