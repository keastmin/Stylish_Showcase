"""Read-only evaluation checks for the authored BASTION animation pack.

Run in the current Blender with exec(compile(..., 'validate_animation_pack.py', 'exec')).
Only writes validation_report.json. Restores action, pose, custom IK values and frame.
"""
import bpy
import json
import math
import os
import traceback
from mathutils import Vector

WORK = r'C:\Users\kemin\Unity\ProjectR\.codex\bastion-pack-work'
REPORT_PATH = os.path.join(WORK, 'validation_report.json')
EXPECTED = ['Idle', 'Walk', 'BladeSlash', 'GatlingFire', 'MissileLaunch']
ANCHOR_TOLERANCE = 0.0001
LOOP_TOLERANCE = 0.0001
GROUND_TOLERANCE = 0.015


def finite(values):
    return all(math.isfinite(float(v)) for v in values)


def flat(matrix):
    return [x for row in matrix for x in row]


def matrix_error(a, b):
    return max(abs(a[r][c] - b[r][c]) for r in range(4) for c in range(4))


def fcurves(action):
    """Blender 4.4+/5 layered actions and older legacy actions."""
    found = []
    try:
        found.extend(action.fcurves)
    except (AttributeError, RuntimeError):
        pass
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                found.extend(bag.fcurves)
    seen = set()
    for curve in found:
        ident = curve.as_pointer()
        if ident not in seen:
            seen.add(ident)
            yield curve


def evaluated_mesh_world_points(obj, depsgraph, indices=None):
    ev = obj.evaluated_get(depsgraph)
    vertices = ev.data.vertices
    return [ev.matrix_world @ vertices[i].co for i in (indices if indices is not None else range(len(vertices)))]


def range3(values):
    return [max(v[i] for v in values) - min(v[i] for v in values) for i in range(3)] if values else [0, 0, 0]


def report_issue(level, code, text, **details):
    report[level].append(dict(code=code, message=text, **details))


report = dict(schemaVersion=1, rig='BASTION_03_RIG',
              units='metres', frameRate=None, groundZ=0,
              thresholds=dict(cableAnchorMetres=ANCHOR_TOLERANCE,
                              loopMatrix=LOOP_TOLERANCE,
                              solePenetrationMetres=GROUND_TOLERANCE,
                              deepBladePenetrationMetres=0.03),
              failures=[], warnings=[], weights={}, actions=[],
              scope='All integer animation frames; cable endpoints, skeleton transforms, loop closure, sole and blade height. Does not prove absence of all armor self-intersections.')
scene = bpy.context.scene
rig = bpy.data.objects.get('BASTION_03_RIG')
collection = bpy.data.collections.get('BASTION | Armor & Mechanisms')
if rig is None or collection is None:
    raise RuntimeError('BASTION rig/model collection not present.')

saved_frame, saved_subframe = scene.frame_current, scene.frame_subframe
saved_action = rig.animation_data.action if rig.animation_data else None
saved_slot = getattr(rig.animation_data, 'action_slot', None) if rig.animation_data else None
saved_pose_position = rig.data.pose_position
saved_pose = {p.name:dict(matrix=p.matrix_basis.copy(), mode=p.rotation_mode,
                        ik=p.get('IK Influence')) for p in rig.pose.bones}
saved_nla = [(t, t.mute) for t in rig.animation_data.nla_tracks] if rig.animation_data else []

try:
    report['frameRate'] = scene.render.fps / scene.render.fps_base
    if abs(report['frameRate'] - 30) > 1e-6:
        report_issue('warnings', 'frame_rate', 'Pack was designed for 30 fps.', observed=report['frameRate'])
    with open(os.path.join(WORK, 'cable_spec.json'), encoding='utf-8') as stream:
        specs = json.load(stream)
    meshes = [o for o in collection.all_objects if o.type == 'MESH']
    rig.animation_data_create()
    for track, _ in saved_nla:
        track.mute = True
    rig.data.pose_position = 'POSE'
    deform = {b.name for b in rig.data.bones if b.use_deform}
    total_vertices = max_weights = 0
    missing_weights = bad_sum = over_limit = 0
    bad_examples = []
    for obj in meshes:
        mods = [m for m in obj.modifiers if m.type == 'ARMATURE' and m.object == rig]
        if not mods:
            report_issue('failures', 'missing_skin', 'Model mesh has no modifier targeting the rig.', mesh=obj.name)
            continue
        indices = {g.index:g.name for g in obj.vertex_groups}
        for vert in obj.data.vertices:
            total_vertices += 1
            weights = [float(g.weight) for g in vert.groups if indices.get(g.group) in deform and g.weight > 1e-8]
            count, total = len(weights), sum(weights)
            max_weights = max(max_weights, count)
            if not weights: missing_weights += 1
            if not finite(weights) or abs(total - 1) > 1e-5: bad_sum += 1
            if count > 4: over_limit += 1
            if (not weights or abs(total-1) > 1e-5 or count > 4) and len(bad_examples) < 12:
                bad_examples.append(dict(mesh=obj.name, vertex=vert.index, influenceCount=count, weightSum=total))
    report['weights'] = dict(meshCount=len(meshes), vertexCount=total_vertices,
                             maxInfluences=max_weights, unweightedVertices=missing_weights,
                             invalidWeightSums=bad_sum, verticesOverFourInfluences=over_limit,
                             examples=bad_examples)
    if missing_weights or bad_sum or over_limit:
        report_issue('failures', 'skin_weights', 'Some skin weights violate normalized, four-influence limits.', **report['weights'])

    soles = {side:bpy.data.objects.get('Foot rubber sole '+side) for side in ('L','R')}
    blade_meshes = [o for o in meshes if o.get('rig_bone') == 'weapon.L' and 'honed' in o.name.lower()]
    if not blade_meshes:
        blade_meshes = [o for o in meshes if o.get('rig_bone') == 'weapon.L']
    rest_inv = {b.name:b.matrix_local.inverted() for b in rig.data.bones}
    actions = []
    for label in EXPECTED:
        action = bpy.data.actions.get('BASTION_'+label) or bpy.data.actions.get('BASTION_03_'+label)
        if action is None:
            report_issue('failures', 'missing_action', 'Required animation is absent.', action=label)
        else:
            actions.append((label, action))

    for label, action in actions:
        rig.animation_data.action = action
        if hasattr(rig.animation_data, 'action_slot') and len(getattr(action,'slots',[])):
            rig.animation_data.action_slot = action.slots[0]
        start, end = (int(round(v)) for v in action.frame_range)
        frames = list(range(start, end+1))
        ar = dict(name=action.name, startFrame=start, endFrame=end,
                  sampledFrames=len(frames), durationSeconds=(end-start)/report['frameRate'],
                  loopExpected=label in ('Idle','Walk'), curves=0, keyframes=0,
                  minKeyframesPerCurve=None, maxKeyframesPerCurve=0,
                  cableMaxEndpointError=0, cableWorst=None,
                  nonfiniteTransformCount=0, maxFootIKTargetError=0,
                  feet={}, minBladeZ=None, minBladeFrame=None)
        counts=[]
        for fc in fcurves(action):
            ar['curves'] += 1
            counts.append(len(fc.keyframe_points))
            ar['keyframes'] += len(fc.keyframe_points)
            if any(not finite((k.co.x,k.co.y)) for k in fc.keyframe_points):
                report_issue('failures','nonfinite_curve','Animation curve contains nonfinite keys.',action=action.name,path=fc.data_path)
        if counts:
            ar['minKeyframesPerCurve'], ar['maxKeyframesPerCurve']=min(counts),max(counts)
        else:
            report_issue('failures','no_curves','Action has no discoverable animation curves.',action=action.name)
        feet_data={side:dict(points=[],minima=[],tracking=[],frames=[]) for side in ('L','R')}
        endpoint_matrices=[]
        for frame in frames:
            scene.frame_set(frame)
            rig.update_tag()
            bpy.context.view_layer.update()
            depsgraph=bpy.context.evaluated_depsgraph_get()
            evrig=rig.evaluated_get(depsgraph)
            for pb in evrig.pose.bones:
                if not finite(flat(pb.matrix)):
                    ar['nonfiniteTransformCount'] += 1
            if frame in (start,end):
                endpoint_matrices.append({p.name:p.matrix.copy() for p in evrig.pose.bones})
            for spec in specs:
                obj=bpy.data.objects.get(spec['name'])
                if obj is None:
                    raise RuntimeError('Missing repaired cable: '+spec['name'])
                side_count=spec['sides']
                for endpoint,bone in enumerate(spec['bones']):
                    first=0 if endpoint==0 else (spec['rings']-1)*side_count
                    points=evaluated_mesh_world_points(obj,depsgraph,range(first,first+side_count))
                    actual=sum(points,Vector())/side_count
                    expected=evrig.matrix_world @ evrig.pose.bones[bone].matrix @ rest_inv[bone] @ Vector(spec['endpoints'][endpoint])
                    err=(actual-expected).length
                    if err>ar['cableMaxEndpointError']:
                        ar['cableMaxEndpointError']=err
                        ar['cableWorst']=dict(mesh=obj.name, endpoint=endpoint, frame=frame)
            for side in ('L','R'):
                pb=evrig.pose.bones['foot.'+side]
                target=evrig.pose.bones['CTRL_foot.'+side]
                actual=evrig.matrix_world @ pb.matrix.translation
                desired=evrig.matrix_world @ target.matrix.translation
                err=(actual-desired).length
                ar['maxFootIKTargetError']=max(ar['maxFootIKTargetError'],err)
                feet_data[side]['points'].append(list(actual))
                feet_data[side]['tracking'].append(err)
                feet_data[side]['frames'].append(frame)
                if soles[side]:
                    feet_data[side]['minima'].append(min(p.z for p in evaluated_mesh_world_points(soles[side],depsgraph)))
            for blade in blade_meshes:
                minimum=min(p.z for p in evaluated_mesh_world_points(blade,depsgraph))
                if ar['minBladeZ'] is None or minimum<ar['minBladeZ']:
                    ar['minBladeZ']=minimum; ar['minBladeFrame']=frame

        if len(endpoint_matrices)==2:
            ar['loopMaxMatrixError']=max(matrix_error(endpoint_matrices[0][name],endpoint_matrices[1][name]) for name in endpoint_matrices[0])
            if ar['loopExpected'] and ar['loopMaxMatrixError']>LOOP_TOLERANCE:
                report_issue('failures','loop_not_closed','First and last evaluated skeleton poses differ.',action=action.name,error=ar['loopMaxMatrixError'])
        if ar['cableMaxEndpointError']>ANCHOR_TOLERANCE:
            report_issue('failures','cable_anchor','Cable endpoint left its socket bone.',action=action.name,error=ar['cableMaxEndpointError'],worst=ar['cableWorst'])
        if ar['nonfiniteTransformCount']:
            report_issue('failures','nonfinite_pose','Evaluated bone matrices contain NaN or infinity.',action=action.name,count=ar['nonfiniteTransformCount'])
        if ar['maxFootIKTargetError']>.005:
            report_issue('warnings','foot_ik_reach','Foot IK cannot precisely reach the authored target.',action=action.name,maxError=ar['maxFootIKTargetError'])
        for side,data in feet_data.items():
            minima=data['minima']
            foot=dict(anklePositionRange=range3(data['points']),minSoleZ=min(minima) if minima else None,
                      maxSoleZ=max(minima) if minima else None,
                      groundContactFrameCount=sum(abs(z)<=.005 for z in minima))
            if label=='Walk':
                # In-place locomotion translates the planted foot backwards at
                # belt speed. Check its constant velocity and level contact,
                # rather than incorrectly expecting a stationary world foot.
                offset=0 if side=='L' else .5
                phases=[((f-start)/(end-start)+offset)%1 for f in frames]
                contact=[i for i,p in enumerate(phases) if p<.60-1e-8]
                drift=[]
                for i in contact:
                    if i==0 or i-1 not in contact or phases[i]<phases[i-1]:
                        continue
                    displacement=Vector(data['points'][i])-Vector(data['points'][i-1])
                    expected_y=.44/.60/(end-start)
                    drift.append((displacement-Vector((0,expected_y,0))).length)
                foot['stanceSampleCount']=len(contact)
                foot['maxStanceVelocityResidualMetresPerFrame']=max(drift) if drift else None
                foot['maxStanceSoleDistanceFromGround']=max(abs(minima[i]) for i in contact) if minima and contact else None
                if drift and max(drift)>.005:
                    report_issue('warnings','walk_stance_slip','Planted foot differs from constant backward in-place travel.',action=action.name,side=side,maxResidualPerFrame=max(drift))
                if minima and contact and max(abs(minima[i]) for i in contact)>.01:
                    report_issue('warnings','walk_stance_height','Support foot is not level with ground throughout stance.',action=action.name,side=side,maxHeightError=max(abs(minima[i]) for i in contact))
            ar['feet'][side]=foot
            if minima and min(minima)<-GROUND_TOLERANCE:
                report_issue('failures','foot_ground_penetration','Sole enters ground beyond tolerance.',action=action.name,side=side,minZ=min(minima))
            if label!='Walk' and max(foot['anklePositionRange'])>.025:
                report_issue('warnings','stationary_foot_drift','Stationary clip moves a foot significantly; inspect contact deliberately.',action=action.name,side=side,axisRanges=foot['anklePositionRange'])
        if ar['minBladeZ'] is not None and ar['minBladeZ']<-.03:
            report_issue('failures','blade_ground_penetration','Blade deeply penetrates ground.',action=action.name,minZ=ar['minBladeZ'],frame=ar['minBladeFrame'])
        elif ar['minBladeZ'] is not None and ar['minBladeZ']<-.005:
            report_issue('warnings','blade_ground_contact','Blade grazes below ground; inspect visually.',action=action.name,minZ=ar['minBladeZ'],frame=ar['minBladeFrame'])
        report['actions'].append(ar)
except Exception as exc:
    report_issue('failures','validator_exception',str(exc),traceback=traceback.format_exc())
finally:
    rig.animation_data.action=saved_action
    if saved_action and saved_slot and hasattr(rig.animation_data,'action_slot'):
        rig.animation_data.action_slot=saved_slot
    for track,mute in saved_nla:
        track.mute=mute
    rig.data.pose_position=saved_pose_position
    scene.frame_set(saved_frame,subframe=saved_subframe)
    for name,snapshot in saved_pose.items():
        pb=rig.pose.bones.get(name)
        if pb:
            pb.rotation_mode=snapshot['mode']
            pb.matrix_basis=snapshot['matrix']
            if snapshot['ik'] is not None: pb['IK Influence']=snapshot['ik']
    rig.update_tag()
    bpy.context.view_layer.update()
    report['passed']=not report['failures']
    with open(REPORT_PATH,'w',encoding='utf-8') as stream:
        json.dump(report,stream,indent=2,ensure_ascii=False)
    print(json.dumps(dict(passed=report['passed'],reportPath=REPORT_PATH,
                          failures=report['failures'],warnings=report['warnings'],
                          checkedActions=len(report['actions']),
                          checkedFrames=sum(a['sampledFrames'] for a in report['actions'])),indent=2,ensure_ascii=False))
