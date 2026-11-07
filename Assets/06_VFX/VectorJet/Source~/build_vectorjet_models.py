"""VectorJet editable Blender source. Run in an isolated Blender --background process.

Meshes intentionally use Unity-native coordinates: X right, Y up, Z exhaust.
FBX axis metadata is Y-up and space conversion is disabled, preserving vertices.
"""
import bpy
import bmesh
import math
import os
import json
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
MODELS = os.path.join(ROOT, 'Models')
SOURCE = os.path.join(ROOT, 'Source~')
DOCS = os.path.join(ROOT, 'Documentation')
for p in [MODELS, SOURCE, DOCS]:
    os.makedirs(p, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, metal=0, rough=.4, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    p.inputs['Emission Color'].default_value = (*color, 1)
    p.inputs['Emission Strength'].default_value = emission
    return m

graphite = material('Nozzle_Graphite', (.028, .04, .057), .88, .30)
titanium = material('Nozzle_Titanium', (.21, .26, .32), .9, .25)
ceramic = material('Nozzle_Ceramic', (.011, .016, .022), .52, .48)
heated = material('Nozzle_HeatTint', (.16, .095, .075), .78, .29)
cyan = material('Nozzle_CyanIndicator', (.02, .30, .58), .4, .26, 2.0)
coremat = material('VFX_Core_Preview', (.22, .7, 1), .0, .5, 2)
shockmat = material('VFX_Shock_Preview', (.72, .9, 1), 0, .5, 3)

def surf(name, profiles, n=48, mat=None, ripple=0):
    verts=[]; faces=[]; uvs=[]
    # Duplicate seam vertex makes the UV seam exact and prevents texture interpolation across it.
    for z,r in profiles:
        for j in range(n+1):
            a=2*math.pi*j/n
            rr=r*(1+ripple*math.sin(8*a+z*9))
            verts.append((rr*math.cos(a), rr*math.sin(a), z))
            uvs.append((j/n,z))
    for i in range(len(profiles)-1):
        for j in range(n):
            a=i*(n+1)+j
            faces.append((a,a+1,a+n+2,a+n+1))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    uv=mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        poly.use_smooth=True
        for li in poly.loop_indices:
            uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
    if mat: mesh.materials.append(mat)
    return obj

core_prof=[]
def column_radius(t):
    anchors=[(0.0,.15),(.35,.135),(.70,.12),(1.0,.09)]
    for (ta,ra),(tb,rb) in zip(anchors,anchors[1:]):
        if t<=tb:
            f=(t-ta)/(tb-ta)
            f=f*f*(3-2*f)
            return ra+(rb-ra)*f
    return anchors[-1][1]
for i in range(41):
    t=i/40
    # A slightly convergent parallel jet column, never a spear-shaped cone.
    # The open tail must disappear through texture/shader opacity, not geometry.
    core_prof.append((t,column_radius(t)))
core=surf('JetCore',core_prof,48,coremat,.018)

cells=[]
for idx,(center,half,width) in enumerate([(.28,.07875,.09),(.505,.075,.083),(.71,.06375,.062),(.89,.0525,.043)]):
    p=[]
    for i in range(17):
        t=i/16
        z=center-half+2*half*t
        radius=max(.00015,width*(math.sin(math.pi*t)**1.35))
        p.append((z,radius))
    cells.append(surf('ShockCell_%02d'%idx,p,48,shockmat))
bpy.ops.object.select_all(action='DESELECT')
for o in cells: o.select_set(True)
bpy.context.view_layer.objects.active=cells[0]
bpy.ops.object.join()
shock=bpy.context.object
shock.name='JetShockCells'
shock.data.name='JetShockCells'

nozzle_parts=[]
def ring(name,profile,mat,segments=64,inside=False):
    o=surf(name,profile,segments,mat)
    if inside:
        bm=bmesh.new();bm.from_mesh(o.data)
        bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
        bm.to_mesh(o.data);bm.free()
    nozzle_parts.append(o)
    return o

ring('OuterChamber',[(-.46,.20),(-.45,.225),(-.25,.225),(-.17,.20),(-.155,.196)],graphite)
ring('RearMount',[(-.48,.22),(-.475,.248),(-.428,.248),(-.423,.22)],titanium)
ring('RearMountInner',[(-.48,.165),(-.48,.22),(-.423,.22),(-.423,.165)],ceramic)
ring('Collar',[(-.28,.227),(-.273,.241),(-.232,.241),(-.225,.222)],titanium)
ring('StatusRing',[(-.312,.226),(-.309,.232),(-.297,.232),(-.294,.226)],cyan)
ring('ThroatLiner',[(-.35,.147),(-.15,.156),(-.02,.149),(0,.150)],ceramic,inside=True)
ring('MouthLip',[(-.024,.151),(-.009,.16),(.001,.16),(.005,.15),(-.006,.146)],heated)
ring('InnerHotRing',[(-.035,.149),(-.03,.152),(-.021,.152),(-.016,.149)],cyan)

def bevel(o, amount=.003, segments=2):
    bpy.context.view_layer.objects.active=o
    mod=o.modifiers.new('Machined edge radii','BEVEL'); mod.width=amount; mod.segments=segments
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in o.data.polygons: p.use_smooth=False

# Separate individual convergent petals with shallow raised heat shields and visible seam gaps.
for j in range(16):
    a=2*math.pi*j/16
    half=math.pi/16*.86
    levels=[(-.238,.222),(-.172,.213),(-.068,.181),(-.008,.169)]
    verts=[]
    for z,r in levels:
        for rr in [r,r-.018]:
            for aa in [a-half,a+half]:
                verts.append((rr*math.cos(aa),rr*math.sin(aa),z))
    faces=[]
    for k in range(len(levels)-1):
        b=4*k; c=b+4
        faces.extend([(b,c,c+1,b+1),(b+2,b+3,c+3,c+2),(b,b+2,c+2,c),(b+1,c+1,c+3,b+3)])
    faces.extend([(0,1,3,2),(12,14,15,13)])
    m=bpy.data.meshes.new('Petal');m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new('Petal_%02d'%j,m);bpy.context.collection.objects.link(o)
    o.data.materials.append(titanium if j%2==0 else graphite); bevel(o,.002,2); nozzle_parts.append(o)
    # Short radial fastening heads on the outer collar, six-sided for readable silhouette.
    bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.016,depth=.012,location=(.241*math.cos(a),.241*math.sin(a),-.252))
    b=bpy.context.object;b.name='CollarBolt_%02d'%j
    b.rotation_euler=Vector((math.cos(a),math.sin(a),0)).to_track_quat('Z','Y').to_euler()
    b.data.materials.append(ceramic)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bevel(b,.001,1);nozzle_parts.append(b)
    # Longitudinal ventilation grooves on rear housing.
    aa=a+.05
    bpy.ops.mesh.primitive_cube_add(size=1,location=(.225*math.cos(aa),.225*math.sin(aa),-.36))
    v=bpy.context.object;v.name='CoolingRail_%02d'%j
    v.dimensions=(.010,.014,.086);v.rotation_euler[2]=aa
    v.data.materials.append(ceramic)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bevel(v,.002,2);nozzle_parts.append(v)

bpy.ops.object.select_all(action='DESELECT')
for o in nozzle_parts: o.select_set(True)
bpy.context.view_layer.objects.active=nozzle_parts[0]
bpy.ops.object.join()
nozzle=bpy.context.object
nozzle.name='JetNozzle';nozzle.data.name='JetNozzle'
bpy.context.scene.cursor.location=(0,0,0)
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)

def export_fbx(o, name):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODELS,name+'.fbx'),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',use_space_transform=False,
        bake_space_transform=False,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,
        path_mode='AUTO')

for o,n in [(core,'JetCore'),(shock,'JetShockCells'),(nozzle,'JetNozzle')]: export_fbx(o,n)

report={'axis':'Vertices in Unity-native +Z exhaust, +Y up. FBX space transform disabled; Y-up/-Z-forward metadata. No parent transforms.',
        'units':'meters','origin':'nozzle outlet plane at z=0',
        'uv':'UV.x circumference 0..1, UV.y global normalized axial Z (all VFX meshes).',
        'models':{}}
for o in [core,shock,nozzle]:
    coords=[v.co for v in o.data.vertices]
    report['models'][o.name]={'vertices':len(coords),'polygons':len(o.data.polygons),
        'bounds_min':[min(v[i] for v in coords) for i in range(3)],
        'bounds_max':[max(v[i] for v in coords) for i in range(3)],
        'materials':[m.name for m in o.data.materials]}
with open(os.path.join(DOCS,'BlenderModels.json'),'w') as f: json.dump(report,f,indent=2)

# Save an editable source with meshes in their exact exported coordinate system.
bpy.ops.object.select_all(action='DESELECT');nozzle.select_set(True)
bpy.context.view_layer.objects.active=nozzle
core.hide_render=True;shock.hide_render=True
scene=bpy.context.scene
scene.world.color=(.025,.025,.025)
scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs['Color'].default_value=(.022,.032,.052,1)
bg.inputs['Strength'].default_value=.35

def area(name,pos,power,color,size,target):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
    o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);o.location=pos
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(1.4,-1.3,1.4),130,(.65,.82,1),2,(0,0,-.2))
area('Rim',(-1.1,.9,.4),190,(.1,.42,1),1,(0,0,-.2))
area('WarmFill',(.1,-.3,-1.6),100,(1,.5,.21),1,(0,0,-.25))
camdata=bpy.data.cameras.new('NozzleCamera');camera=bpy.data.objects.new('NozzleCamera',camdata)
bpy.context.collection.objects.link(camera);camera.location=(.84,-1.13,.73)
camera.rotation_euler=(Vector((0,0,-.19))-camera.location).to_track_quat('-Z','Y').to_euler()
camdata.type='ORTHO';camdata.ortho_scale=1.02;scene.camera=camera
scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
try: scene.render.engine='BLENDER_EEVEE'
except TypeError: pass
scene.render.filepath=os.path.join(DOCS,'NozzleModel.png')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'VectorJet.blend'))
bpy.ops.render.render(write_still=True)
print('VECTORJET_MODELS_READY '+json.dumps(report))
