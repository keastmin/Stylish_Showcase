"""Author native Shader Graph serialization using schemas installed in this project.
No live Unity session needed; asset import performs native Shader Graph validation.
"""
import copy, json, pathlib, uuid

ROOT=pathlib.Path(__file__).parent
EXPORT=ROOT.parent if ROOT.name=='Source~' else ROOT/'Export'
def uid(): return uuid.uuid4().hex
def ref(x): return {'m_Id':x['m_ObjectId'] if isinstance(x,dict) else x}
def read_many(path):
    s=path.read_text(encoding='utf-8-sig'); dec=json.JSONDecoder(); result=[]
    while s.strip():
        s=s.lstrip(); o,n=dec.raw_decode(s); result.append(o); s=s[n:]
    return result
template=read_many(pathlib.Path('Assets/09_Shaders/Dodge Trail Effect/Dodge Trail Shader Graph.shadergraph'))
byid={o['m_ObjectId']:o for o in template}
objects=[]; nodes=[]; edges=[]; props=[]
def add(o): objects.append(o); return o
def clone(o):
    o=copy.deepcopy(o); o['m_ObjectId']=uid(); return o
def slot(kind,number,name,out=False,value=0):
    s={'m_SGVersion':0,'m_Type':'UnityEditor.ShaderGraph.'+kind+'MaterialSlot','m_ObjectId':uid(),
       'm_Id':number,'m_DisplayName':name,'m_SlotType':1 if out else 0,'m_Hidden':False,'m_HideConnector':False,
       'm_ShaderOutputName':name,'m_StageCapability':3,'m_CustomBinding':''}
    if kind.startswith('Texture2D'):
        s['m_BareResource']=False
        if kind=='Texture2DInput':s.update(m_Texture={'m_SerializedTexture':'','m_Guid':''},m_DefaultType=0)
    else:
        n=int(kind[-1]) if kind.startswith('Vector') else 1
        s['m_Value']=value if n==1 else {k:(value[i] if isinstance(value,(list,tuple)) and i<len(value) else 0) for i,k in enumerate('xyzw'[:n])}
        s['m_DefaultValue']=copy.deepcopy(s['m_Value']);s['m_Labels']=[]
    return add(s)
def node(kind,name,slots,x=0,y=0,version=0,**kw):
    n={'m_SGVersion':version,'m_Type':'UnityEditor.ShaderGraph.'+kind,'m_ObjectId':uid(),'m_Group':{'m_Id':''},'m_Name':name,
       'm_DrawState':{'m_Expanded':True,'m_Position':{'serializedVersion':'2','x':x,'y':y,'width':220,'height':160}},
       'm_Slots':[ref(s) for s in slots],'synonyms':[],'m_Precision':1,'m_PreviewExpanded':False,'m_DismissedVersion':0,'m_PreviewMode':0,'m_CustomColors':{'m_SerializableColors':[]}}
    n.update(kw);nodes.append(n);return add(n)
def connect(a,ai,b,bi):edges.append({'m_OutputSlot':{'m_Node':ref(a),'m_SlotId':ai},'m_InputSlot':{'m_Node':ref(b),'m_SlotId':bi}})

root=clone(template[0]);root.update(m_Properties=[],m_CategoryData=[],m_Nodes=[],m_GroupDatas=[],m_StickyNoteDatas=[],m_Edges=[],m_Path='ProjectR/Vector Jet',m_Keywords=[],m_Dropdowns=[],m_SubDatas=[])
vertices=[];fragments=[]
for old in template:
    if old['m_Type'].endswith('BlockNode') and old['m_Name']!='SurfaceDescription.AlphaClipThreshold':
        n=clone(old);n['m_Slots']=[]
        for sr in old['m_Slots']:
            s=add(clone(byid[sr['m_Id']]));n['m_Slots'].append(ref(s))
        add(n);nodes.append(n)
        (vertices if n['m_Name'].startswith('Vertex') else fragments).append(n)
root['m_VertexContext']['m_Blocks']=[ref(n) for n in vertices]
root['m_FragmentContext']['m_Blocks']=[ref(n) for n in fragments]
root['m_FragmentContext']['m_Position']={'x':600,'y':300}

target=clone(next(o for o in template if o['m_Type'].endswith('.UniversalTarget')))
subtarget=clone(next(o for o in template if o['m_Type'].endswith('.UniversalUnlitSubTarget')))
target.update(m_ActiveSubTarget=ref(subtarget),m_AllowMaterialOverride=False,m_SurfaceType=1,m_ZWriteControl=2,m_AlphaMode=2,m_RenderFace=0,m_AlphaClip=False,m_CastShadows=False,m_ReceiveShadows=False,m_SupportVFX=True)
add(target);add(subtarget);root['m_ActiveTargets']=[ref(target)]

baseprop=next(o for o in template if o['m_Type'].endswith('Vector1ShaderProperty'))
def property_node(name,kind,value,y):
    p=clone(baseprop); p['m_Type']='UnityEditor.ShaderGraph.Internal.'+kind+'ShaderProperty';p['m_SGVersion']=0 if kind=='Texture2D' else 1
    p.update(m_Guid={'m_GuidSerialized':str(uuid.uuid4())},m_Name=name,m_DefaultRefNameVersion=1,m_RefNameGeneratedByDisplayName=name,m_DefaultReferenceName='_'+name,m_OverrideReferenceName='_'+name,m_FloatType=0)
    if kind=='Texture2D':
        p.update(m_Value={'m_SerializedTexture':'','m_Guid':''},isMainTexture=False,useTilingAndOffset=False,useTexelSize=False,isHDR=False,m_Modifiable=True,m_DefaultType=0)
    elif kind=='Vector1':p['m_Value']=value
    else:p['m_Value']={k:(value[i] if i<len(value) else 0) for i,k in enumerate('xyzw')}
    add(p);props.append(p)
    n=node('PropertyNode',name,[slot('Texture2D' if kind=='Texture2D' else kind,0,'Out',True,value)],-800,y,m_Property=ref(p))
    return n

mask=property_node('FlameMask','Texture2D',0,0)
tint=property_node('Tint','Vector3',[2,4.5,12],180)
opacity=property_node('Opacity','Vector1',.24,320)
uvscale=property_node('UVScale','Vector2',[.26,.84],460)
uvbias=property_node('UVBias','Vector2',[.37,.08],600)
shockmode=property_node('ShockMode','Vector1',0,1240)
uv=node('UVNode','Mesh UV',[slot('Vector4',0,'Out',True,[0,0,0,0])],-800,760,m_OutputChannel=0)
normal=node('NormalVectorNode','World Normal',[slot('Vector3',0,'Out',True,[0,0,1])],-800,920,m_Space=2)
view=node('ViewDirectionNode','World View Direction',[slot('Vector3',0,'Out',True,[0,0,1])],-800,1080,version=1,m_Space=2)
cs=[slot('Texture2DInput',0,'Mask'),slot('Vector2',1,'UV'),slot('Vector3',2,'Normal'),slot('Vector3',3,'View'),slot('Vector3',4,'Tint'),slot('Vector1',5,'Opacity'),slot('Vector2',6,'UVScale'),slot('Vector2',7,'UVBias'),slot('Vector1',10,'ShockMode'),slot('Vector3',8,'Color',True),slot('Vector1',9,'Alpha',True)]
body='''float2 sampleUV;
sampleUV.x = UVBias.x + UVScale.x * (0.5 + 0.5 * sin(6.28318530718 * UV.x));
sampleUV.y = UV.y * UVScale.y + UVBias.y;
float luminance = SAMPLE_TEXTURE2D(Mask.tex, Mask.samplerstate, sampleUV).r;
float facing = abs(dot(normalize(Normal), normalize(View)));
float softVolume = pow(saturate(facing), 1.5);
float ends = smoothstep(0.0, 0.045, UV.y) * (1.0 - smoothstep(0.72, 1.0, UV.y));
float4 cellDistance = (UV.y - float4(0.28, 0.505, 0.71, 0.89)) / float4(0.035, 0.033, 0.029, 0.025);
float4 cellDensity = exp(-0.5 * cellDistance * cellDistance);
float pressureDensity = max(max(cellDensity.x, cellDensity.y), max(cellDensity.z, cellDensity.w));
float axialSoftness = lerp(1.0, pressureDensity, saturate(ShockMode));
Color = Tint * luminance;
Alpha = Opacity * softVolume * ends * axialSoftness;'''
fn=node('CustomFunctionNode','Jet Soft Volume (Custom Function)',cs,-200,160,version=1,m_SourceType=1,m_FunctionName='JetSoftVolume',m_FunctionSource='',m_FunctionSourceUsePragmas=True,m_FunctionBody=body)
for n,i in [(mask,0),(uv,1),(normal,2),(view,3),(tint,4),(opacity,5),(uvscale,6),(uvbias,7),(shockmode,10)]:connect(n,0,fn,i)
connect(fn,8,next(n for n in fragments if n['m_Name'].endswith('BaseColor')),0)
connect(fn,9,next(n for n in fragments if n['m_Name'].endswith('Alpha')),0)
root['m_Nodes']=[ref(n) for n in nodes];root['m_Properties']=[ref(p) for p in props];root['m_Edges']=edges
dest=EXPORT/'Shaders/JetVolume.shadergraph';dest.parent.mkdir(parents=True,exist_ok=True)
dest.write_text('\n\n'.join(json.dumps(o,indent=4) for o in [root]+objects)+'\n',encoding='utf-8')
allids={o['m_ObjectId'] for o in [root]+objects}
assert len(allids)==len(objects)+1
for edge in edges:
    for side in ('m_OutputSlot','m_InputSlot'):assert edge[side]['m_Node']['m_Id'] in allids
print(f'Authored {dest}: {len(nodes)} nodes, {len(props)} properties, {len(edges)} edges; URP VFX additive soft volume.')
