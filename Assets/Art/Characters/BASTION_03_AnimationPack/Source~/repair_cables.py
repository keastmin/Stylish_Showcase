import bpy, math, json, os
from mathutils import Vector, Matrix

WORK = r'C:\Users\kemin\Unity\ProjectR\.codex\bastion-pack-work'
rig = bpy.data.objects['BASTION_03_RIG']
collection = bpy.data.collections['BASTION | Armor & Mechanisms']
if not os.path.exists(os.path.join(WORK, 'BASTION_03_before_cable_repair.blend')):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, 'BASTION_03_before_cable_repair.blend'), copy=True)

def enum(owner, name, value):
    vals = [x.identifier for x in owner.bl_rna.properties[name].enum_items]
    assert value in vals, (name, value, vals)
    return value

materials = {m.name[:2]:m for m in bpy.data.materials}
dark, steel, accent = materials['03'], materials['04'], materials['06']

def skin(obj, bone):
    obj.parent = rig
    obj.matrix_world = Matrix.Identity(4)
    obj['rig_bone'] = bone
    vg = obj.vertex_groups.new(name=bone)
    vg.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    mod = obj.modifiers.new('BASTION Cable Skin', 'ARMATURE')
    mod.object = rig
    return obj

def mesh_object(name, verts, faces, material):
    me = bpy.data.meshes.new(name + '_Mesh')
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    collection.objects.link(ob)
    me.materials.append(material)
    return ob

def cylinder(name, a, b, radius, material, bone, sides=16):
    a,b=Vector(a),Vector(b)
    tangent=(b-a).normalized()
    u=tangent.cross(Vector((1,0,0))).normalized()
    v=tangent.cross(u).normalized()
    verts=[p + radius*(math.cos(i*math.tau/sides)*u + math.sin(i*math.tau/sides)*v) for p in (a,b) for i in range(sides)]
    faces=[tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    ob=skin(mesh_object(name,verts,faces,material),bone)
    for f in ob.data.polygons: f.use_smooth=len(f.vertices)==4
    return ob

def port(name, endpoint, bone, radius):
    p=Vector(endpoint)
    # Socket flange penetrates the armor; the cable terminates inside its collar.
    cylinder(name+' | armor flange', p-Vector((0,.115,0)),p-Vector((0,.064,0)),radius*1.95,dark,bone,8)
    cylinder(name+' | hex coupling', p-Vector((0,.070,0)),p-Vector((0,.019,0)),radius*1.55,steel,bone,8)
    cylinder(name+' | copper lock ring', p-Vector((0,.030,0)),p-Vector((0,.010,0)),radius*1.32,accent,bone)
    cylinder(name+' | strain relief', p-Vector((0,.015,0)),p+Vector((0,.016,0)),radius*1.13,dark,bone)

def hermite(p0,p1,m0,m1,t):
    return (2*t**3-3*t*t+1)*p0 + (t**3-2*t*t+t)*m0 + (-2*t**3+3*t*t)*p1 + (t**3-t*t)*m1

def cable(name, knots, bones, radius):
    old=bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    pts=[Vector(x) for x in knots]
    tangents=[Vector((0,.25,0))] + [(pts[i+1]-pts[i-1])*.5 for i in range(1,len(pts)-1)] + [Vector((0,-.25,0))]
    centers=[]
    for k in range(len(pts)-1):
        for j in range(12): centers.append(hermite(pts[k],pts[k+1],tangents[k],tangents[k+1],j/12))
    centers.append(pts[-1]); sides=12; verts=[]; faces=[]
    for i,p in enumerate(centers):
        tangent=(centers[min(i+1,len(centers)-1)]-centers[max(i-1,0)]).normalized()
        u=tangent.cross(Vector((1,0,0))).normalized(); v=tangent.cross(u).normalized()
        for j in range(sides): verts.append(p + radius*(math.cos(j*math.tau/sides)*u + math.sin(j*math.tau/sides)*v))
    for i in range(len(centers)-1):
        for j in range(sides): faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    faces += [tuple(reversed(range(sides))),tuple(range((len(centers)-1)*sides,len(centers)*sides))]
    ob=mesh_object(name,verts,faces,dark); ob.parent=rig
    g0=ob.vertex_groups.new(name=bones[0]); g1=ob.vertex_groups.new(name=bones[1])
    for i in range(len(centers)):
        t=i/(len(centers)-1); u=max(0,min(1,(t-.15)/.7)); w=u*u*(3-2*u)
        inds=list(range(i*sides,(i+1)*sides))
        if w<1: g0.add(inds,1-w,'REPLACE')
        if w>0: g1.add(inds,w,'REPLACE')
    mod=ob.modifiers.new('Two-ended flexible cable skin','ARMATURE'); mod.object=rig
    for f in ob.data.polygons: f.use_smooth=len(f.vertices)==4
    ob['cable_bones']=list(bones); ob['cable_endpoints']=list(pts[0])+list(pts[-1]); ob['cable_sides']=sides
    ob['repair_note']='Both terminal rings rigidly locked to their own socket bone; smooth normalized two-bone flex between ends.'
    port(name+' | A',pts[0],bones[0],radius)
    port(name+' | B',pts[-1],bones[1],radius)
    return {'name':name,'bones':bones,'endpoints':[list(pts[0]),list(pts[-1])],'rings':len(centers),'sides':sides}

# Remove only fittings from a previous invocation of this repair script.
for ob in list(collection.objects):
    if ' | A | ' in ob.name or ' | B | ' in ob.name:
        if any(ob.name.startswith(s) for s in ['Arm power loom','Dorsal armored cable']): bpy.data.objects.remove(ob,do_unlink=True)

reports=[]
for side,s in [('L',1),('R',-1)]:
    reports.append(cable('Arm power loom '+side,[(s*1.02,.15,2.07),(s*1.17,.29,2.04),(s*1.28,.31,1.85),(s*1.18,.27,1.66),(s*1.07,.135,1.66)],('upper_arm.'+side,'forearm.'+side),.027))
for name,s in [('Dorsal armored cable',-1),('Dorsal armored cable.001',1)]:
    reports.append(cable(name,[(s*.20,.315,1.51),(s*.30,.55,1.55),(s*.32,.70,1.72),(s*.25,.72,1.89),(s*.19,.64,1.91)],('pelvis','spine'),.034))
rig['cable_repair']='4 dual-anchored flexible cables / 8 armor sockets'
bpy.context.view_layer.update()
open(os.path.join(WORK,'cable_spec.json'),'w',encoding='utf-8').write(json.dumps(reports,indent=2))
print(json.dumps({'repaired':reports,'socket_count':8},indent=2))
