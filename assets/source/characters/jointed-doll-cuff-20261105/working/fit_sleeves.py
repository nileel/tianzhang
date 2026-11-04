"""Fit the generated hollow sleeve to the existing doll's forearms in its bind pose."""
import math

import bpy
import bmesh
from mathutils import Matrix, Vector


# Raw FBX longitudinal extrema (elbow entry nearest +Y). The X/Z centre
# is the inscribed bore detected through the entire mesh along that axis.
ELBOW_Y, WRIST_Y = .345707, -.345707
BORE_X, BORE_Z = -.00147, .78545


def fit_point(p, side):
    across = (p.x - BORE_X) * .71
    up = (p.z - BORE_Z) * 1.10
    # Keep the worn bore at full scale. Past the sleeve walls, fit the
    # hanging fold backwards and lengthwise along the character's forearm.
    across = math.copysign(min(abs(across), .075) + max(abs(across) - .075, 0) * .57, across)
    drop = max(-up - .080, 0)
    up += drop * .79
    up = min(up, .080) + max(up - .080, 0) * .47
    along = .447 + .231 * (ELBOW_Y - p.y) / (ELBOW_Y - WRIST_Y) + drop * .39
    angle = math.radians(22)
    y = -.026 - .024 * (along - .447) + across * math.sin(angle) - up * math.cos(angle)
    z = 1.360 - .070 * (along - .447) - across * math.cos(angle) - up * math.sin(angle)
    return Vector((side * along, y, z))


def apply(scene, raw):
    rig = scene.objects['JointedDollRig']
    # The static Tripo FBX carries 24 fps. The doll's original actions use 30.
    scene.render.fps = 30
    scene.render.fps_base = 1
    for label, sign in [('L', 1), ('R', -1)]:
        target = scene.objects['sleeve.' + label]
        old_mesh = target.data
        target.vertex_groups.clear()
        target.data = raw.data.copy()
        target.data.name = 'HollowSleeve.' + label
        points = [raw.matrix_world @ v.co for v in raw.data.vertices]
        mesh = target.data
        if mesh.has_custom_normals:
            mesh.attributes.remove(mesh.attributes['custom_normal'])
        for v, point in zip(mesh.vertices, points):
            v.co = fit_point(point, sign)
        # The left fitting reverses handedness. Reverse the winding with
        # the face loops to preserve per-corner UVs on the mirrored cloth.
        if sign == 1:
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
            bm.to_mesh(mesh)
            bm.free()
        mesh.update()
        group = target.vertex_groups.new(name='forearm.' + label)
        group.add(list(range(len(mesh.vertices))), 1.0, 'REPLACE')
        assert target.parent == rig and target.modifiers[0].object == rig
        if old_mesh.users == 0:
            bpy.data.meshes.remove(old_mesh)
    mat = raw.data.materials[0]
    for node in mat.node_tree.nodes:
        if node.type == 'BSDF_PRINCIPLED':
            node.inputs['Roughness'].default_value = .85
            node.inputs['Metallic'].default_value = 0.
    bpy.context.view_layer.update()


def allow_for_sleeve_width(scene):
    """Open the low arm poses enough to clear the hip and the four robe panels."""
    rig = scene.objects['JointedDollRig']
    saved_action = rig.animation_data.action
    saved_frame = scene.frame_current
    for action in bpy.data.actions:
        rig.animation_data.action = action
        for frame in range(1, int(action.frame_range.y) + 1):
            scene.frame_set(frame)
            for label, sign in [('L', 1), ('R', -1)]:
                arm = rig.pose.bones['upper_arm.' + label]
                wrist = rig.pose.bones['hand.' + label].head
                low = max(0., min(1., (arm.head.z - wrist.z - .08) / .23))
                weight = low * low * (3 - 2 * low)
                rest = arm.parent.bone.matrix_local.inverted() @ arm.bone.matrix_local
                basis = (arm.parent.matrix @ rest).to_quaternion()
                tilt = Matrix.Rotation(math.radians(-sign * 12.5 * weight), 3, 'Y').to_quaternion()
                arm.rotation_quaternion = basis.inverted() @ tilt @ basis @ arm.rotation_quaternion
                arm.keyframe_insert('rotation_quaternion', frame=frame, group=arm.name)
    rig.animation_data.action = saved_action
    scene.frame_set(saved_frame)
