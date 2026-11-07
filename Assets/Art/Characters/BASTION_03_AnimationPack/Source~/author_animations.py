"""Author BASTION's five 30 fps character actions in the current scene.

Run in the live Blender after cable repairs. Does not save, export, change
selection, remove unrelated animation, or touch the separate missile demo.
The arm gestures are authored in armature axes, then conjugated into each
bone's actual rest basis. Feet use the existing calibrated two-bone IK.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector, Euler, Quaternion

FPS = 30
RIG_NAME = 'BASTION_03_RIG'
STAGING = Path(r'C:\Users\kemin\Unity\ProjectR\.codex\bastion-pack-work')
OWNER = 'BASTION Animation Pack 2026-11-07'


def clamp(x, a=0.0, b=1.0):
    return min(b, max(a, x))


def ease(x):
    x = clamp(x)
    return x * x * (3.0 - 2.0 * x)


def smoother(x):
    x = clamp(x)
    return x * x * x * (x * (x * 6.0 - 15.0) + 10.0)


def envelope(frame, start, attack_end, release_start, end):
    return ease((frame - start) / (attack_end - start)) * (
        1.0 - ease((frame - release_start) / (end - release_start)))


def keyvalue(frame, keys):
    """Smooth authored pose interpolation, scalar or tuple values."""
    if frame <= keys[0][0]:
        return keys[0][1]
    for (f0, a), (f1, b) in zip(keys, keys[1:]):
        if frame <= f1:
            t = ease((frame - f0) / (f1 - f0))
            if isinstance(a, (tuple, list)):
                return tuple(x + (y - x) * t for x, y in zip(a, b))
            return a + (b - a) * t
    return keys[-1][1]


def base_pose():
    # The blade remains slightly forward for clearance above the ground.
    return {
        'rot': {'spine': (0.65, 0, 0), 'neck': (-0.65, 0, 0),
                'upper_arm.L': (-3, -1.5, 0), 'forearm.L': (-3, 0, 0),
                'weapon.L': (-2, 0, 0),
                'upper_arm.R': (-2, 1.5, 0), 'forearm.R': (-3, 0, 0),
                'weapon.R': (4.35, 0, 0)},
        'pos': {'pelvis': (0, 0, -0.025)},
        'local_rot': {},
    }


def addrot(pose, bone, value):
    old = pose['rot'].get(bone, (0, 0, 0))
    pose['rot'][bone] = tuple(a + b for a, b in zip(old, value))


def sample_idle(frame, end=121):
    pose = base_pose()
    phase = math.tau * (frame - 1) / (end - 1)
    s, c = math.sin(phase), math.cos(phase)
    pose['pos']['pelvis'] = (0.008 * s, 0, -0.025 + 0.004 * (1 - c))
    addrot(pose, 'spine', (0.35 * s, 0.30 * s, 0.45 * s))
    addrot(pose, 'neck', (-0.20 * s, 0, -0.65 * s))
    addrot(pose, 'head', (0.45 * math.sin(phase * 2), 0, 3.2 * s))
    addrot(pose, 'upper_arm.L', (0.45 * s, -0.2 * s, 0))
    addrot(pose, 'forearm.L', (-0.35 * s, 0, 0))
    addrot(pose, 'upper_arm.R', (-0.35 * s, 0.2 * s, 0))
    addrot(pose, 'rack.L', (0.13 * s, 0, 0))
    addrot(pose, 'rack.R', (-0.13 * s, 0, 0))
    return pose


def gait_foot(phase):
    # A 60% support interval creates a clear double support phase.
    phase %= 1.0
    stride = 0.44
    if phase < 0.60:
        return (-stride / 2 + stride * phase / 0.60, 0.0)
    swing = (phase - 0.60) / 0.40
    return (stride / 2 - stride * smoother(swing),
            0.105 * math.sin(math.pi * swing) ** 2)


def sample_walk(frame, end=49):
    pose = base_pose()
    phase = (frame - 1) / (end - 1)
    angle = math.tau * phase
    # Down at weight acceptance; no vertical motion is assigned to root.
    pose['pos']['pelvis'] = (0.045 * math.sin(angle), -0.018,
                             -0.060 + 0.020 * math.cos(2 * angle))
    pose['rot']['pelvis'] = (0.5, 0, 2.5 * math.sin(angle))
    addrot(pose, 'spine', (1.6, -1.2 * math.sin(angle), -4.0 * math.sin(angle)))
    addrot(pose, 'head', (-1.0, 0, 1.0 * math.sin(angle)))
    for side, offset, sign in [('L', 0.0, 1), ('R', 0.5, -1)]:
        y, z = gait_foot(phase + offset)
        pose['pos']['CTRL_foot.' + side] = (0, y, z)
        pose['pos']['CTRL_knee.' + side] = (0, y * 0.30, z * 0.15)
        # Restrained arms keep the very long blade clear of the torso.
        addrot(pose, 'upper_arm.' + side,
               (sign * 5.5 * math.sin(angle), 0, 0))
        addrot(pose, 'forearm.' + side,
               (-1.5 - sign * 2.5 * math.sin(angle), 0, 0))
        addrot(pose, 'rack.' + side, (0.45 * math.sin(angle * 2), 0, 0))
    return pose


def sample_blade(frame, end=73):
    pose = base_pose()
    # Wind-up, one clear held anticipation, short striking arc, overshoot,
    # and a longer servo-controlled recovery. Feet remain planted.
    keys = [
        (1, (0, 0, 0)), (13, (-35, -9, 3)),
        (25, (-102, -14, 8)), (30, (-106, -14, 9)),
        (38, (-20, -15, -16)), (44, (-12, -11, -18)),
        (56, (-18, -5, -4)), (73, (0, 0, 0))]
    addrot(pose, 'upper_arm.L', keyvalue(frame, keys))
    addrot(pose, 'forearm.L', keyvalue(frame, [
        (1, (0, 0, 0)), (13, (-18, 0, 0)), (25, (-35, 0, 0)),
        (30, (-37, 0, 0)), (38, (-6, 0, 0)), (44, (3, 0, 0)),
        (56, (-8, 0, 0)), (73, (0, 0, 0))]))
    addrot(pose, 'weapon.L', keyvalue(frame, [
        (1, (0, 0, 0)), (25, (-5, 0, 0)), (30, (-7, 0, 0)),
        (39, (-17, 0, -12)), (46, (-8, 0, -7)), (73, (0, 0, 0))]))
    addrot(pose, 'spine', keyvalue(frame, [
        (1, (0, 0, 0)), (25, (-2.5, -1.0, 8.0)), (30, (-3.0, -1.0, 8.5)),
        (39, (5.0, 1.0, -10)), (46, (4.0, 1.0, -8)), (73, (0, 0, 0))]))
    addrot(pose, 'head', keyvalue(frame, [
        (1, (0, 0, 0)), (25, (0, 0, -6)), (39, (-2, 0, 8)), (73, (0, 0, 0))]))
    addrot(pose, 'upper_arm.R', keyvalue(frame, [
        (1, (0, 0, 0)), (26, (-8, 5, 0)), (41, (8, 4, 2)), (73, (0, 0, 0))]))
    pose['pos']['pelvis'] = keyvalue(frame, [
        (1, (0, 0, -.025)), (25, (.025, .015, -.065)),
        (30, (.025, .015, -.067)), (39, (-.015, -.045, -.085)),
        (46, (-.01, -.03, -.060)), (73, (0, 0, -.025))])
    return pose


def spin_angle(frame):
    """Integrated smooth spin-up, constant fire, smooth brake; radians."""
    # The barrel has sixfold symmetry, so finishing on a whole revolution
    # avoids a visual jump to the common ready pose.
    total = 15.0 * math.tau
    a, b, c, d = 15.0, 30.0, 68.0, 88.0
    max_speed = total / ((b - a) / 2 + c - b + (d - c) / 2)
    if frame <= a:
        return 0.0
    if frame < b:
        u = (frame - a) / (b - a)
        return max_speed * (b - a) * (u ** 3 - 0.5 * u ** 4)
    first = max_speed * (b - a) / 2
    if frame <= c:
        return first + max_speed * (frame - b)
    second = first + max_speed * (c - b)
    if frame < d:
        u = (frame - c) / (d - c)
        return second + max_speed * (d - c) * (u - u ** 3 + .5 * u ** 4)
    return total


def sample_gatling(frame, end=97):
    pose = base_pose()
    brace = envelope(frame, 1, 23, 72, 97)
    firing = envelope(frame, 28, 32, 67, 71)
    # High-frequency recoil is mechanically small and does not teleport
    # weapon location or cause knee/foot vibration.
    recoil = firing * (0.5 + 0.5 * math.cos(math.tau * (frame - 32) / 4))
    pose['pos']['pelvis'] = (-.025 * brace, .025 * brace, -.025 - .055 * brace)
    addrot(pose, 'spine', (2.5 * brace - .40 * recoil, 0, -2.0 * brace))
    addrot(pose, 'head', (-1.0 * brace, 0, 1.5 * brace))
    addrot(pose, 'upper_arm.R', (-6.0 * brace + .60 * recoil, 2.0 * brace, 0))
    addrot(pose, 'forearm.R', (-9.0 * brace + 1.0 * recoil, 0, 0))
    addrot(pose, 'weapon.R', (12.5 * brace + .30 * recoil, 0, 0))
    addrot(pose, 'upper_arm.L', (-4.0 * brace, -2.0 * brace, 0))
    addrot(pose, 'forearm.L', (-3.0 * brace, 0, 0))
    pose['local_rot']['barrel.R'] = (0, spin_angle(frame), 0)
    return pose


def sample_missile(frame, end=121):
    pose = base_pose()
    brace = envelope(frame, 1, 22, 83, 121)
    left = envelope(frame, 14, 35, 76, 102)
    right = envelope(frame, 20, 41, 82, 108)
    # Each lid extends forward from a rear hinge. Negative world X opens up.
    pose['rot']['hatch.L'] = (-104 * left, 0, 0)
    pose['rot']['hatch.R'] = (-104 * right, 0, 0)
    impulse = (math.exp(-max(0, frame - 48) / 5) * math.sin(max(0, frame - 48) * .85)
               if 48 <= frame < 72 else 0.0)
    impulse += (0.70 * math.exp(-max(0, frame - 60) / 5) * math.sin(max(0, frame - 60) * .85)
                if 60 <= frame < 84 else 0.0)
    pose['pos']['pelvis'] = (0, .015 * brace, -.025 - .045 * brace - .004 * abs(impulse))
    addrot(pose, 'spine', (-1.2 * brace + .65 * impulse, 0, 0))
    addrot(pose, 'head', (-7 * brace, 0, 0))
    addrot(pose, 'upper_arm.L', (-4.0 * brace, -2.5 * brace, 0))
    addrot(pose, 'upper_arm.R', (-3.0 * brace, 2.5 * brace, 0))
    addrot(pose, 'forearm.L', (-3.0 * brace, 0, 0))
    addrot(pose, 'rack.L', (.65 * impulse, 0, 0))
    addrot(pose, 'rack.R', (.45 * impulse, 0, 0))
    return pose


CLIPS = [
    {'name': 'BASTION_Idle', 'end': 121, 'loop': True, 'sample': sample_idle,
     'description': 'Servo idle, optical scanning and subtle power-cycle weight shift.', 'events': []},
    {'name': 'BASTION_Walk', 'end': 49, 'loop': True, 'sample': sample_walk,
     'description': 'In-place heavy patrol with 60% support and 0.105 m foot clearance.',
     'recommended_speed_mps': 0.44 / (0.60 * 1.60),
     'events': [{'frame': 1, 'name': 'LeftFootContact'}, {'frame': 25, 'name': 'RightFootContact'}]},
    {'name': 'BASTION_BladeSlash', 'end': 73, 'loop': False, 'sample': sample_blade,
     'description': 'Anticipated overhead diagonal cleave, impact follow-through and recovery.',
     'events': [{'frame': 33, 'name': 'BladeHitWindowStart'},
                {'frame': 39, 'name': 'BladeHitWindowEnd'}]},
    {'name': 'BASTION_GatlingFire', 'end': 97, 'loop': False, 'sample': sample_gatling,
     'description': 'Brace, barrel spin-up, sustained recoil burst, spin-down and ready.',
     'events': [{'frame': 32, 'name': 'GatlingFireStart'}, {'frame': 68, 'name': 'GatlingFireEnd'}]},
    {'name': 'BASTION_MissileLaunch', 'end': 121, 'loop': False, 'sample': sample_missile,
     'description': 'Staggered hatch opening, paired launch impulses, hatch closing and recovery.',
     'events': [{'frame': 48, 'name': 'LaunchMissileLeft'}, {'frame': 60, 'name': 'LaunchMissileRight'}]},
]


def apply_pose(rig, pose):
    """Set a deterministic pose. All translations use armature rest axes."""
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
        if 'IK Influence' in pb:
            pb['IK Influence'] = 1.0 if pb.name.startswith('CTRL_foot.') else 0.0
    for name, xyz in pose['pos'].items():
        pb = rig.pose.bones[name]
        pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector(xyz)
    for name, xyz in pose['rot'].items():
        pb = rig.pose.bones[name]
        rest = pb.bone.matrix_local.to_quaternion()
        rotation = Euler(tuple(math.radians(a) for a in xyz), 'XYZ').to_quaternion()
        pb.rotation_euler = (rest.inverted() @ rotation @ rest).to_euler('XYZ')
    for name, xyz in pose['local_rot'].items():
        rig.pose.bones[name].rotation_euler = xyz


def action_curves(action):
    # Both legacy and layered Actions are supported without depending on a
    # particular Action Slot API version.
    found = set()
    if hasattr(action, 'fcurves'):
        for fc in action.fcurves:
            found.add(fc.as_pointer())
            yield fc
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                for fc in bag.fcurves:
                    if fc.as_pointer() not in found:
                        found.add(fc.as_pointer())
                        yield fc


def build_actions():
    rig = bpy.data.objects[RIG_NAME]
    scene = bpy.context.scene
    rig.animation_data_create()
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    metadata = {'rig': rig.name, 'fps': FPS, 'root_motion': False,
                'units': 'metres', 'forward': '-Y', 'up': '+Z', 'clips': [],
                'note': 'Event labels are timing metadata, not mandatory Unity AnimationEvent callbacks.'}
    for spec in CLIPS:
        old = bpy.data.actions.get(spec['name'])
        if old is not None:
            if old.get('authoring_owner') != OWNER:
                raise RuntimeError('Refusing to overwrite unrelated Action: ' + spec['name'])
            if rig.animation_data.action == old:
                rig.animation_data.action = None
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(spec['name'])
        action.use_fake_user = True
        action['authoring_owner'] = OWNER
        action['loop'] = spec['loop']
        action['description'] = spec['description']
        rig.animation_data.action = action
        for frame in range(1, spec['end'] + 1):
            # Setting the frame before resetting the pose avoids the active
            # action overwriting freshly assigned values on frame changes.
            scene.frame_set(frame)
            apply_pose(rig, spec['sample'](frame, spec['end']))
            for pb in rig.pose.bones:
                pb.keyframe_insert(data_path='location', frame=frame, group=pb.name)
                pb.keyframe_insert(data_path='rotation_euler', frame=frame, group=pb.name)
                pb.keyframe_insert(data_path='scale', frame=frame, group=pb.name)
                if 'IK Influence' in pb:
                    pb.keyframe_insert(data_path='["IK Influence"]', frame=frame, group=pb.name)
        # Dense samples must not receive automatic Bezier overshoot.
        for curve in action_curves(action):
            for key in curve.keyframe_points:
                key.interpolation = 'LINEAR'
        action['event_timing_json'] = json.dumps(spec['events'])
        metadata['clips'].append({
            **{k: v for k, v in spec.items() if k != 'sample'},
            'start': 1, 'seconds': (spec['end'] - 1) / FPS,
        })
        print('Authored', action.name, 'frames 1..', spec['end'])
    rig.animation_data.action = bpy.data.actions['BASTION_Idle']
    scene.frame_start = 1
    scene.frame_end = 121
    scene.frame_set(1)
    bpy.context.view_layer.update()
    STAGING.mkdir(parents=True, exist_ok=True)
    (STAGING / 'animation_metadata.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    return metadata


if __name__ == '__main__':
    print(json.dumps(build_actions(), indent=2))
