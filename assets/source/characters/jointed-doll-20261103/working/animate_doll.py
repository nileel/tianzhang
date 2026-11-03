"""Author three rigid-doll actions on the existing rig; run through Blender MCP.

Reads the actual rest joints. Does not change mesh topology, weights or rest bones.
World convention: Z up, forward -Y, character left +X. Frame 1 is double support.
"""
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path('D:/天章游戏开发/assets/source/characters/jointed-doll-20261103')
RIG_NAME = 'JointedDollRig'
CLIPS = {'walk': 48, 'cast': 72, 'attack': 60}
MESH_NAMES = ['Body', 'sleeve.L', 'sleeve.R', 'front_panel.L',
              'front_panel.R', 'rear_panel.L', 'rear_panel.R']
rig = bpy.data.objects[RIG_NAME]
scene = bpy.context.scene
assert rig.type == 'ARMATURE' and bpy.context.mode == 'OBJECT'
assert len(rig.data.bones) == 23, 'Expected the approved 23-bone doll rig.'
assert all(abs(rig.matrix_world[i][j] - (1.0 if i == j else 0.0)) < 1e-5
           for i in range(4) for j in range(4)), 'Rig must retain identity object transform.'
meshes = [bpy.data.objects[name] for name in MESH_NAMES]
assert all(obj.type == 'MESH' and obj.parent == rig for obj in meshes)
assert not any(pb.constraints for pb in rig.pose.bones), 'Unexpected pose constraints.'
assert not any(name in bpy.data.actions for name in CLIPS), 'Action names already exist; inspect before replacing.'

rest_rot = {b.name: b.matrix_local.to_quaternion() for b in rig.data.bones}
rest_head = {b.name: b.head_local.copy() for b in rig.data.bones}
rest_vec = {b.name: b.tail_local - b.head_local for b in rig.data.bones}
ordered = sorted(rig.pose.bones, key=lambda b: len(b.parent_recursive))
identity = Quaternion((1.0, 0.0, 0.0, 0.0))
pelvis_offset = Vector((0.0, 0.0, -0.035))  # Constant knee clearance; no root bob.
rig.animation_data_create()
assert not rig.animation_data.nla_tracks, 'Unexpected NLA tracks on the target rig.'


def turn(axis, degrees):
    return Quaternion(Vector(axis), math.radians(degrees))


def rx(degrees):
    return turn((1, 0, 0), degrees)


def ry(degrees):
    return turn((0, 1, 0), degrees)


def rz(degrees):
    return turn((0, 0, 1), degrees)


def smooth(value):
    return value * value * (3.0 - 2.0 * value)


def envelope(t, keys):
    for (ta, va), (tb, vb) in zip(keys, keys[1:]):
        if t <= tb:
            return va + (vb - va) * smooth(max(0.0, (t - ta) / (tb - ta)))
    return keys[-1][1]


def leg_ik(side, ankle):
    """Two fixed-length links, knees bending toward -Y, without moving the root."""
    thigh, shin = 'thigh.' + side, 'shin.' + side
    hip = rest_head[thigh] + pelvis_offset
    upper, lower = rest_vec[thigh].length, rest_vec[shin].length
    delta = ankle - hip
    distance = delta.length
    assert abs(upper - lower) < distance < upper + lower, 'Foot target is unreachable.'
    axis = delta.normalized()
    bend = Vector((0, -1, 0))
    bend = (bend - axis * bend.dot(axis)).normalized()
    along = (upper * upper - lower * lower + distance * distance) / (2 * distance)
    knee = hip + axis * along + bend * math.sqrt(max(0.0, upper * upper - along * along))
    thigh_delta = rest_vec[thigh].normalized().rotation_difference((knee - hip).normalized())
    shin_delta = rest_vec[shin].normalized().rotation_difference((ankle - knee).normalized())
    return thigh_delta, shin_delta, knee


def footstep(phase):
    """Flat planted half-cycle followed by a 55 mm swing; all transitions eased."""
    phase %= 1.0
    stride = 0.145
    if phase < 0.5:
        u = phase * 2.0
        return -stride + 2.0 * stride * smooth(u), 0.0
    u = (phase - 0.5) * 2.0
    return stride * math.cos(math.pi * u), 0.055 * math.sin(math.pi * u) ** 2


def make_pose(clip, t):
    # The values below are desired cumulative rotations in armature/global space.
    delta = {b.name: identity.copy() for b in rig.data.bones}
    if clip == 'walk':
        phase = 2.0 * math.pi * t
        delta['spine'] = rz(2.0 * math.sin(phase))
        delta['chest'] = rz(-3.0 * math.sin(phase)) @ delta['spine']
        effort = 0.0
    elif clip == 'cast':
        effort = envelope(t, [(0, 0), (.2, .35), (.43, 1), (.62, 1), (.86, .15), (1, 0)])
        delta['spine'] = rx(1.5 * effort)
        delta['chest'] = rx(3.0 * effort) @ delta['spine']
    else:
        wind = envelope(t, [(0, 0), (.24, 1), (.42, 0), (1, 0)])
        strike = envelope(t, [(0, 0), (.25, 0), (.46, 1), (.6, .8), (.86, .12), (1, 0)])
        effort = max(wind, strike)
        delta['spine'] = rz(15 * wind - 10 * strike)
        delta['chest'] = rz(7 * wind - 5 * strike) @ rx(4 * strike) @ delta['spine']
    # Long rear hair follows the chest. No independent neck or head animation.
    delta['head'] = delta['chest'].copy()

    for side, sign in [('L', 1), ('R', -1)]:
        ankle = rest_head['foot.' + side].copy()
        if clip == 'walk':
            travel, lift = footstep(t + (0.0 if side == 'L' else 0.5))
            ankle.y += travel
            ankle.z += lift
            swing = sign * 17.0 * math.cos(phase)
            upper = rx(swing) @ delta['chest'] @ ry(sign * 70)
            forearm = rx(-10.0 - 4.0 * max(0.0, -sign * math.sin(phase))) @ upper
            sleeve_pitch = -sign * 5.0 * math.cos(phase - .35)
        elif clip == 'cast':
            ankle.y += -0.035 if side == 'L' else 0.035
            upper = rx(-58 * effort) @ delta['chest'] @ ry(sign * (70 - 8 * effort))
            forearm = rx(-10 - 30 * effort) @ upper
            sleeve_pitch = -4 * effort
        else:
            ankle.y += -0.045 if side == 'R' else 0.045
            if side == 'R':
                upper = rx(20 * wind - 73 * strike) @ delta['chest'] @ ry(-70 + 23 * wind + 8 * strike)
                forearm = rx(-10 - 22 * wind - 12 * strike) @ upper
            else:
                upper = rx(-16 * wind - 22 * strike) @ delta['chest'] @ ry(70)
                forearm = rx(-10 - 20 * effort) @ upper
            sleeve_pitch = (5 * wind - 6 * strike) if side == 'R' else -3 * effort
        delta['upper_arm.' + side] = upper
        delta['forearm.' + side] = forearm
        delta['hand.' + side] = forearm.copy()  # Entire rigid hand.
        # Absolute down-oriented sleeve target cancels inherited arm rotation.
        delta['sleeve.' + side] = rx(sleeve_pitch)
        thigh, shin, knee = leg_ik(side, ankle)
        delta['thigh.' + side] = thigh
        delta['shin.' + side] = shin
        delta['foot.' + side] = identity.copy()  # Preserve the rest sole orientation.
        # Static +/-10-degree panel tilt is already baked in the meshes: do not repeat it.
        # Clearance follows the actual leg surface envelope, not the thigh's
        # angle: knee flexion alone must not throw both panels forward.
        hip = rest_head['thigh.' + side] + pelvis_offset
        samples = [(hip.lerp(knee, .35), .075), (knee, .065), (ankle, .055)]
        front_mount = rest_head['front_panel.' + side] + pelvis_offset
        rear_mount = rest_head['rear_panel.' + side] + pelvis_offset
        front_total = max(math.degrees(math.atan2(front_mount.y - p.y + radius + .012,
                                                  front_mount.z - p.z)) for p, radius in samples)
        rear_total = max(math.degrees(math.atan2(p.y + radius + .012 - rear_mount.y,
                                                 rear_mount.z - p.z)) for p, radius in samples)
        front_clearance = max(0.0, front_total - 10.0)
        rear_clearance = max(0.0, rear_total - 10.0)
        delta['front_panel.' + side] = rx(-front_clearance)
        delta['rear_panel.' + side] = rx(rear_clearance)
    return delta


def apply_pose(delta, previous):
    for pb in ordered:
        name = pb.name
        parent_delta = identity if pb.parent is None else delta[pb.parent.name]
        # R_pose = D * R_rest; child basis removes the parent's accumulated D.
        q = rest_rot[name].inverted() @ parent_delta.inverted() @ delta[name] @ rest_rot[name]
        q.normalize()
        if name in previous and q.dot(previous[name]) < 0:
            q.negate()
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = q
        pb.location = rest_rot[name].inverted() @ pelvis_offset if name == 'hips' else (0, 0, 0)
        pb.scale = (1, 1, 1)
        previous[name] = q.copy()


scene.render.fps = 30
scene.render.fps_base = 1.0
actions = {}
for clip, count in CLIPS.items():
    action = bpy.data.actions.new(clip)
    action.use_fake_user = True
    action['source_rig'] = RIG_NAME
    rig.animation_data.action = action
    previous = {}
    for frame in range(1, count + 1):
        scene.frame_set(frame)
        t = 0.0 if frame == count else (frame - 1) / (count - 1)
        apply_pose(make_pose(clip, t), previous)
        for pb in ordered:
            pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
            pb.keyframe_insert('location', frame=frame, group=pb.name)
    actions[clip] = action

rig.animation_data.action = actions['walk']
scene.frame_start = 1
scene.frame_end = CLIPS['walk']
scene.frame_set(1)
bpy.context.view_layer.update()
for obj in bpy.context.selected_objects:
    obj.select_set(False)
rig.select_set(True)
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = rig
blend_path = ROOT / 'working/fuyuan-jointed-doll.blend'
fbx_path = ROOT / 'export/fuyuan-jointed-doll.fbx'
fbx_path.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
bpy.ops.export_scene.fbx(
    filepath=str(fbx_path), use_selection=True, object_types={'ARMATURE', 'MESH'},
    axis_forward='-Z', axis_up='Y', global_scale=1.0, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',
    use_mesh_modifiers=True, use_triangles=True, add_leaf_bones=False, use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    path_mode='COPY', embed_textures=True)
result = {'rig': rig.name, 'meshes': [obj.name for obj in meshes],
          'actions': {name: list(action.frame_range) for name, action in actions.items()},
          'fps': 30, 'blend': str(blend_path), 'fbx': str(fbx_path),
          'pelvis_constant_offset_m': list(pelvis_offset),
          'note': 'Analytic foot targets and rigid-part animation; inspect actual surfaces in runtime.'}
