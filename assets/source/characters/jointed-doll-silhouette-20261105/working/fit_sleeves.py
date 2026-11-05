"""Fit a vertically authored sleeve to the doll, preserving its long silhouette."""
import math
import bpy
import bmesh
from mathutils import Matrix, Vector


def apply(scene, source, collar, scale):
    """collar is the centre of the original elbow rim in FBX world coordinates."""
    points = [source.matrix_world @ v.co for v in source.data.vertices]
    for side, sign in [('L', 1), ('R', -1)]:
        sleeve = scene.objects['sleeve.' + side]
        old_mesh = sleeve.data
        sleeve.vertex_groups.clear()
        sleeve.data = source.data.copy()
        mesh = sleeve.data
        if mesh.has_custom_normals:
            mesh.attributes.remove(mesh.attributes['custom_normal'])
        forearm = sleeve.vertex_groups.new(name='forearm.' + side)
        hanging = sleeve.vertex_groups.new(name='sleeve.' + side)
        for vertex, point in zip(mesh.vertices, points):
            local = point - Vector(collar)
            along = .467 - local.z * scale[2]
            along += max(along - .68, 0.) * .40
            collar_fit = 1.20 - .20 * max(0., min(1., (-local.z - .10) / .24))
            vertex.co = (
                sign * along,
                -.026 + local.y * scale[1] * collar_fit,
                1.360 - .070 * (along - .447) - local.x * scale[0] * collar_fit,
            )
            weight = max(0., min(1., (along - .84) / .21))
            weight = weight * weight * (3 - 2 * weight)
            forearm.add([vertex.index], 1. - weight, 'REPLACE')
            hanging.add([vertex.index], weight, 'REPLACE')
        if sign == 1:
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
            bm.to_mesh(mesh)
            bm.free()
        mesh.update()
        if old_mesh.users == 0:
            bpy.data.meshes.remove(old_mesh)
    for material in source.data.materials:
        for node in material.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                node.inputs['Roughness'].default_value = .85
                node.inputs['Metallic'].default_value = 0.
    scene.render.fps = 30
    scene.render.fps_base = 1.
    bpy.context.view_layer.update()


def allow_for_sleeve_width(scene, degrees=4.0):
    """One low-arm clearance pass; final asset uses two passes from original actions."""
    rig = scene.objects['JointedDollRig']
    saved_action, saved_frame = rig.animation_data.action, scene.frame_current
    for action in bpy.data.actions:
        rig.animation_data.action = action
        for frame in range(1, int(action.frame_range.y) + 1):
            scene.frame_set(frame)
            for side, sign in [('L', 1), ('R', -1)]:
                arm = rig.pose.bones['upper_arm.' + side]
                wrist = rig.pose.bones['hand.' + side].head
                low = max(0., min(1., (arm.head.z - wrist.z - .08) / .23))
                weight = low * low * (3 - 2 * low)
                rest = arm.parent.bone.matrix_local.inverted() @ arm.bone.matrix_local
                basis = (arm.parent.matrix @ rest).to_quaternion()
                tilt = Matrix.Rotation(math.radians(-sign * degrees * weight), 3, 'Y').to_quaternion()
                arm.rotation_quaternion = basis.inverted() @ tilt @ basis @ arm.rotation_quaternion
                arm.keyframe_insert('rotation_quaternion', frame=frame, group=arm.name)
    rig.animation_data.action = saved_action
    scene.frame_set(saved_frame)


def animate_hanging(scene):
    """Use the existing sleeve bones for the cloth beyond the wrist opening."""
    rig = scene.objects['JointedDollRig']
    saved_action, saved_frame = rig.animation_data.action, scene.frame_current
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for side, sign in [('L', 1), ('R', -1)]:
        bone = rig.data.edit_bones['sleeve.' + side]
        bone.head = (sign * .78, -.026, 1.33669)
        bone.tail = (sign * 1.08, -.026, 1.31569)
        bone.roll = 0
    bpy.ops.object.mode_set(mode='OBJECT')
    for action in bpy.data.actions:
        rig.animation_data.action = action
        for frame in range(1, int(action.frame_range.y) + 1):
            scene.frame_set(frame)
            for side, sign in [('L', 1), ('R', -1)]:
                bone = rig.pose.bones['sleeve.' + side]
                base = bone.parent.matrix @ bone.parent.bone.matrix_local.inverted() @ bone.bone.matrix_local
                current = base.to_3x3() @ Vector((0, 1, 0))
                down = (current.normalized() * .55 + Vector((sign * .30, .02, -1)).normalized() * .45).normalized()
                rotation = current.normalized().rotation_difference(down)
                target = rotation @ base.to_quaternion()
                bone.rotation_quaternion = base.to_quaternion().inverted() @ target
                bone.location = (0, 0, 0)
                bone.keyframe_insert('rotation_quaternion', frame=frame, group=bone.name)
                bone.keyframe_insert('location', frame=frame, group=bone.name)
    rig.animation_data.action = saved_action
    scene.frame_set(saved_frame)
