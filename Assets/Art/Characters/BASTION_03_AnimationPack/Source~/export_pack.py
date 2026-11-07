import bpy, os, json, shutil
from pathlib import Path

WORK=Path(r'C:\Users\kemin\Unity\ProjectR\.codex\bastion-pack-work')
OUT=WORK/'Export'
for p in ['Models','Animations/FBX','Source~','Documentation','Editor']:(OUT/p).mkdir(parents=True,exist_ok=True)
rig=bpy.data.objects['BASTION_03_RIG']; root=bpy.data.objects['BASTION_03_ROOT']
scene=bpy.context.scene; collection=bpy.data.collections['BASTION | Armor & Mechanisms']
saved_action=rig.animation_data.action if rig.animation_data else None
saved_frame=scene.frame_current
saved_range=(scene.frame_start,scene.frame_end)
saved_pose=rig.data.pose_position
saved_select=list(bpy.context.selected_objects); saved_active=bpy.context.view_layer.objects.active

def select(obs):
    for o in bpy.context.selected_objects:o.select_set(False)
    for o in obs:o.hide_set(False);o.select_set(True)
    bpy.context.view_layer.objects.active=rig

def export(path, animated):
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,
        object_types={'MESH','ARMATURE','EMPTY'},global_scale=1.0,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=False,
        use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,
        use_armature_deform_only=False,armature_nodetype='NULL',
        bake_anim=animated,bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,bake_anim_step=1.0,bake_anim_simplify_factor=0.0)

clips=[]
try:
    scene.render.fps=30
    # Export a genuine bind pose for the mesh, independent of whichever action is previewed.
    rig.data.pose_position='REST'
    rig.animation_data.action=None
    bpy.context.view_layer.update()
    select([root,rig]+[o for o in collection.all_objects if o.type=='MESH'])
    export(OUT/'Models'/'BASTION_03.fbx',False)
    rig.data.pose_position='POSE'
    for suffix in ['Idle','Walk','BladeSlash','GatlingFire','MissileLaunch']:
        action=bpy.data.actions['BASTION_'+suffix]
        rig.animation_data.action=action
        scene.frame_start=int(action.frame_range[0]);scene.frame_end=int(action.frame_range[1])
        scene.frame_set(scene.frame_start)
        select([root,rig])
        filename='Animations/FBX/BASTION_03_'+suffix+'.fbx'
        export(OUT/filename,True)
        clips.append({'name':suffix,'filename':filename,'loop':suffix in ['Idle','Walk'],
            'fps':30,'startFrame':scene.frame_start,'endFrame':scene.frame_end,
            'duration':(scene.frame_end-scene.frame_start)/30})
    mats=[]
    used={m for o in collection.all_objects if o.type=='MESH' for m in o.data.materials if m}
    for m in sorted(used,key=lambda m:m.name):
        node=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None) if m.use_nodes else None
        def val(name,default):return node.inputs[name].default_value if node and name in node.inputs else default
        mats.append({'name':m.name,'baseColor':list(val('Base Color',m.diffuse_color)),
            'metallic':float(val('Metallic',0)),'roughness':float(val('Roughness',.5)),
            'emission':list(val('Emission Color',(0,0,0,1)))[:3],
            'emissionStrength':float(val('Emission Strength',0))})
    manifest={'name':'BASTION 03 Cable Repair & Animation Pack','version':1,'fps':30,
        'colorSpace':'linear','rigType':'Generic','model':'Models/BASTION_03.fbx',
        'blenderForward':'-Y','unityForward':'+Z','scaleMeters':1,
        'materials':mats,'clips':clips,'cables':json.loads((WORK/'cable_spec.json').read_text(encoding='utf-8')),
        'notes':['4 flexible cables, 8 rigid armor sockets.','Walk is in place; movement speed is controlled by game code.','MissileLaunch animates the launcher and recoil. Spawn projectile/VFX through SOCKET_Missile.L.']}
    if (WORK/'animation_metadata.json').exists():manifest['animationDetails']=json.loads((WORK/'animation_metadata.json').read_text(encoding='utf-8'))
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding='utf-8')
finally:
    rig.data.pose_position=saved_pose
    rig.animation_data.action=saved_action
    scene.frame_start,scene.frame_end=saved_range
    scene.frame_set(saved_frame)
    select(saved_select)
    bpy.context.view_layer.objects.active=saved_active

rig.animation_data.action=bpy.data.actions['BASTION_Idle']
scene.frame_start=1;scene.frame_end=int(rig.animation_data.action.frame_range[1]);scene.frame_set(1)
source=Path(r'C:\Users\kemin\OneDrive\바탕 화면\Blender File\Stylish Showcase Blender')/'BASTION_03_Cabled_Animated.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(source))
shutil.copy2(source,OUT/'Source~'/source.name)
for name in ['repair_cables.py','author_animations.py','validate_animation_pack.py','export_pack.py']:
    if (WORK/name).exists():shutil.copy2(WORK/name,OUT/'Source~'/name)
for name in ['validation_report.json','animation_metadata.json','cable_spec.json']:
    if (WORK/name).exists():shutil.copy2(WORK/name,OUT/'Documentation'/name)
print(json.dumps({'output':str(OUT),'source':str(source),'clips':clips},indent=2))
