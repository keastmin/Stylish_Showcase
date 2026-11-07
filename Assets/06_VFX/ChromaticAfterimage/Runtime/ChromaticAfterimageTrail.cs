using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace ProjectR.VFX
{
    /// <summary>Independent pose snapshots and a world-space GPU particle wake, driven by its caller.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ChromaticAfterimageTrail : MonoBehaviour
    {
        [Header("Source model")]
        public Transform sourceRoot;
        public Material afterimageMaterial;
        public VisualEffect particles;
        [Header("Frozen poses")]
        [Min(0.02f)] public float snapshotInterval = 0.065f;
        [Min(0.1f)] public float snapshotLifetime = 0.64f;
        [Range(2,24)] public int maxSnapshots = 12;
        [Range(0,1)] public float opacity = 0.66f;
        [Min(1)] public float noiseScale = 27f;
        [ColorUsage(true,true)] public Color edgeColor = new Color(0.12f,0.65f,1.8f,1);
        [Range(0,2)] public float edgeStrength = 0.3f;
        public bool includeRigidMeshes = true;
        [Header("GPU wake")]
        [Min(0)] public float fragmentsPerSecond = 720f;
        [Min(0)] public float glintsPerSecond = 90f;
        [Tooltip("Use real time for the standalone preview. Can be disabled when connecting to gameplay later.")]
        public bool useUnscaledTime = true;
        [HideInInspector] public bool externalClock;
        [HideInInspector] public bool externalRendering;

        sealed class Piece
        {
            public Renderer source;
            public Mesh mesh;
            public bool ownsMesh;
            public Matrix4x4 matrix;
            public MaterialPropertyBlock[] properties;
            public bool visible;
        }
        sealed class Snapshot
        {
            public readonly List<Piece> pieces = new List<Piece>();
            public float age;
            public bool alive;
        }
        readonly List<Snapshot> pool = new List<Snapshot>();
        readonly List<Renderer> sources = new List<Renderer>();
        MaterialPropertyBlock overall;
        MaterialPropertyBlock perMaterial;
        Mesh emissionPose;
        readonly CombineInstance[] emissionCombine=new CombineInstance[1];
        Transform cachedRoot;
        bool emitting;
        float emitRemaining, intervalRemaining;
        int nextSnapshot, seed;
        public int ActiveSnapshots { get { int n=0; foreach(var s in pool) if(s.alive) n++; return n; } }
        public bool IsEmitting => emitting;

        void OnEnable() { if (sourceRoot != null) Bind(sourceRoot); }
        void LateUpdate()
        {
            if (!externalClock && Application.isPlaying) Tick(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            if(!externalRendering) DrawSnapshots();
        }
        public void Bind(Transform model)
        {
            Release();
            if(overall==null) overall=new MaterialPropertyBlock();
            if(perMaterial==null) perMaterial=new MaterialPropertyBlock();
            emissionPose=new Mesh {name="World-space GPU emission pose",hideFlags=HideFlags.DontSave};
            emissionPose.MarkDynamic();
            sourceRoot=model; cachedRoot=model;
            if(model==null) return;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                if(r is SkinnedMeshRenderer || (includeRigidMeshes && r is MeshRenderer && r.GetComponent<MeshFilter>()!=null)) sources.Add(r);
            if(particles!=null && particles.visualEffectAsset!=null)
            {
                SetRates(0);
                particles.Stop();
            }
        }
        public void Play(float duration = 0.34f)
        {
            if(sourceRoot==null || afterimageMaterial==null) return;
            if(cachedRoot!=sourceRoot || sources.Count==0) Bind(sourceRoot);
            emitRemaining=Mathf.Max(0.01f,duration); intervalRemaining=0; emitting=true;
            SetRates(1);
            if(particles!=null) particles.Play();
        }
        public void StopEmission() { emitting=false; SetRates(0); if(particles!=null) particles.Stop(); }
        void SetRates(float scale)
        {
            if(particles==null || particles.visualEffectAsset==null) return;
            if(particles.HasFloat("FragmentRate")) particles.SetFloat("FragmentRate",fragmentsPerSecond*scale);
            if(particles.HasFloat("GlintRate")) particles.SetFloat("GlintRate",glintsPerSecond*scale);
            if(particles.HasVector3("BoundsCenter") && sourceRoot!=null) particles.SetVector3("BoundsCenter",sourceRoot.position+Vector3.up);
        }
        public void Tick(float deltaTime)
        {
            float dt=Mathf.Max(0,deltaTime);
            foreach(var s in pool) if(s.alive) { s.age+=dt; if(s.age>=snapshotLifetime) s.alive=false; }
            if(!emitting) return;
            SetRates(1);
            intervalRemaining-=dt;
            // Bake at most one real pose per frame, rather than duplicating a pose after a slow frame.
            if(intervalRemaining<=0) { CapturePose(); intervalRemaining+=snapshotInterval; intervalRemaining=Mathf.Max(0,intervalRemaining); }
            emitRemaining-=dt;
            if(emitRemaining<=0) StopEmission();
        }
        public void CapturePose()
        {
            if(sourceRoot==null || afterimageMaterial==null || sources.Count==0) return;
            Snapshot s;
            if(pool.Count<Mathf.Max(2,maxSnapshots))
            {
                s=new Snapshot();
                foreach(var r in sources)
                {
                    var piece=new Piece {source=r};
                    if(r is SkinnedMeshRenderer)
                    {
                        piece.mesh=new Mesh {name="Chromatic pose (pooled)",hideFlags=HideFlags.DontSave};
                        piece.mesh.MarkDynamic(); piece.ownsMesh=true;
                    }
                    else piece.mesh=r.GetComponent<MeshFilter>().sharedMesh;
                    s.pieces.Add(piece);
                }
                pool.Add(s);
            }
            else { s=pool[nextSnapshot]; nextSnapshot=(nextSnapshot+1)%pool.Count; }
            s.age=0; s.alive=true; seed++;
            bool boundEmission=false;
            foreach(var p in s.pieces)
            {
                var r=p.source;
                p.visible=r!=null && r.enabled && r.gameObject.activeInHierarchy;
                if(!p.visible) continue;
                if(r is SkinnedMeshRenderer skin) skin.BakeMesh(p.mesh,false);
                p.matrix=r.localToWorldMatrix;
                // The same frozen pose drives GPU emission. World-space vertices avoid skin-buffer timing dependencies.
                if(!boundEmission && r is SkinnedMeshRenderer && emissionPose!=null && particles!=null && particles.HasMesh("SourcePose"))
                {
                    emissionCombine[0]=new CombineInstance {mesh=p.mesh,transform=p.matrix};
                    emissionPose.Clear(); emissionPose.CombineMeshes(emissionCombine,true,true,false);
                    particles.SetMesh("SourcePose",emissionPose); boundEmission=true;
                }
                var materials=r.sharedMaterials;
                int count=Mathf.Min(p.mesh.subMeshCount,materials.Length);
                if(p.properties==null || p.properties.Length!=count) { p.properties=new MaterialPropertyBlock[count]; for(int i=0;i<count;i++) p.properties[i]=new MaterialPropertyBlock(); }
                r.GetPropertyBlock(overall);
                for(int i=0;i<count;i++)
                {
                    var m=materials[i]; var b=p.properties[i]; b.Clear(); if(m==null) continue;
                    r.GetPropertyBlock(perMaterial,i);
                    string map=m.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                    string tint=m.HasProperty("_BaseColor")?"_BaseColor":"_Color";
                    Texture tex=m.HasProperty(map)?m.GetTexture(map):null;
                    if(overall.HasProperty(map)) tex=overall.GetTexture(map);
                    if(perMaterial.HasProperty(map)) tex=perMaterial.GetTexture(map);
                    b.SetTexture("_BaseMap",tex!=null?tex:Texture2D.whiteTexture);
                    Color color=m.HasProperty(tint)?m.GetColor(tint):Color.white;
                    if(overall.HasProperty(tint)) color=overall.GetColor(tint);
                    if(perMaterial.HasProperty(tint)) color=perMaterial.GetColor(tint);
                    b.SetColor("_BaseColor",color);
                    Vector2 scale=m.HasProperty(map)?m.GetTextureScale(map):Vector2.one, offset=m.HasProperty(map)?m.GetTextureOffset(map):Vector2.zero;
                    Vector4 st=new Vector4(scale.x,scale.y,offset.x,offset.y);
                    if(overall.HasProperty(map+"_ST")) st=overall.GetVector(map+"_ST");
                    if(perMaterial.HasProperty(map+"_ST")) st=perMaterial.GetVector(map+"_ST");
                    b.SetVector("_BaseMap_ST",st);
                    b.SetTexture("_EmissionMap",m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap")!=null?m.GetTexture("_EmissionMap"):Texture2D.whiteTexture);
                    Color emission=m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor")?m.GetColor("_EmissionColor"):Color.black;
                    if(overall.HasProperty("_EmissionColor")) emission=overall.GetColor("_EmissionColor");
                    if(perMaterial.HasProperty("_EmissionColor")) emission=perMaterial.GetColor("_EmissionColor");
                    b.SetColor("_EmissionColor",emission);
                    b.SetFloat("_Cutoff",m.IsKeywordEnabled("_ALPHATEST_ON") && m.HasProperty("_Cutoff")?m.GetFloat("_Cutoff"):0.001f);
                    b.SetFloat("_Seed",seed*0.731f+i*0.27f);
                }
            }
        }
        public void DrawSnapshots(Camera camera = null)
        {
            if(afterimageMaterial==null) return;
            foreach(var s in pool)
            {
                if(!s.alive) continue;
                float fade=1-Mathf.Clamp01(s.age/Mathf.Max(0.01f,snapshotLifetime));
                foreach(var p in s.pieces)
                {
                    if(!p.visible || p.mesh==null || p.properties==null) continue;
                    for(int i=0;i<p.properties.Length;i++)
                    {
                        var b=p.properties[i]; b.SetFloat("_Fade",fade); b.SetFloat("_Opacity",opacity);
                        b.SetFloat("_NoiseScale",noiseScale); b.SetColor("_EdgeColor",edgeColor); b.SetFloat("_EdgeStrength",edgeStrength);
                        Graphics.DrawMesh(p.mesh,p.matrix,afterimageMaterial,gameObject.layer,camera,i,b,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
                    }
                }
            }
        }
        public void Clear()
        {
            StopEmission(); foreach(var s in pool) s.alive=false;
            if(particles!=null && particles.visualEffectAsset!=null) { particles.Reinit(); particles.Stop(); }
        }
        void OnDisable() { Release(); }
        void Release()
        {
            StopEmission();
            foreach(var s in pool) foreach(var p in s.pieces) if(p.ownsMesh && p.mesh!=null)
            { if(Application.isPlaying) Destroy(p.mesh); else DestroyImmediate(p.mesh); }
            pool.Clear(); sources.Clear(); nextSnapshot=0; cachedRoot=null;
            if(emissionPose!=null) { if(Application.isPlaying) Destroy(emissionPose); else DestroyImmediate(emissionPose); emissionPose=null; }
        }
    }
}
