"""Refit Fu Yuan's four original Tripo robe panels on the existing armature.

Run apply(scene) on a freshly opened 2026-11-03 final doll scene. Object space
is armature space in this file. No external input or new material is needed.
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree


# Width at the original hem, target hem width, top of the cloth, collar cut.
# Heights are in the source panel's unrotated frame, relative to the mount.
PANELS = {
    'front_panel': (.32, .23, -.038, -.033),
    'rear_panel': (.42, .29, -.049, -.044),
}


def close_cut(bm, edges):
    # Both cut surfaces are small. Use the adjacent seam's actual texture UV
    # for each newly exposed top edge rather than sampling the atlas origin.
    uv = bm.loops.layers.uv.active
    adjoining = {face for edge in edges for face in edge.link_faces}
    caps = bmesh.ops.holes_fill(bm, edges=edges)['faces']
    if len(caps) != 2:
        raise RuntimeError(f'Expected two closed halves, got {len(caps)} caps.')
    for face in caps:
        adjacent = next(link for edge in face.edges for link in edge.link_faces
                        if link in adjoining)
        seam_uv = sum((loop[uv].uv for loop in adjacent.loops), Vector((0, 0)))
        seam_uv /= len(adjacent.loops)
        for loop in face.loops:
            loop[uv].uv = seam_uv
        # A cap is flat across the cut and should not smooth the cloth edge.
        face.smooth = False
        for edge in face.edges:
            edge.smooth = False


def fit_panel(scene, prefix, side):
    sign = 1 if side == 'L' else -1
    is_front = prefix == 'front_panel'
    initial_width, final_width, cloth_top, cut_z = PANELS[prefix]
    obj = scene.objects[f'{prefix}.{side}']
    rig = obj.parent
    mount = rig.data.bones[obj.name].head_local
    # Invert the exact placement in the original build_doll.py.
    yaw = math.radians(sign * (32 if is_front else 145))
    old_pitch = math.radians(-10 if is_front else 10)
    old_rot = Matrix.Rotation(old_pitch, 3, 'X') @ Matrix.Rotation(yaw, 3, 'Z')
    inverse = old_rot.transposed()
    mesh = obj.data
    bm = bmesh.new()
    try:
        bm.from_mesh(mesh)
        for v in bm.verts:
            v.co = inverse @ (v.co - mount)
        old_span = max(v.co.x for v in bm.verts) - min(v.co.x for v in bm.verts)
        if abs(old_span - initial_width) > .001:
            raise ValueError(f'{obj.name} does not match the original 2026-11-03 fit.')

        cut = bmesh.ops.bisect_plane(
            bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
            plane_co=(0, 0, cut_z), plane_no=(0, 0, 1), dist=.0000002,
        )
        cut_edges = [e for e in cut['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        bmesh.ops.split_edges(bm, edges=cut_edges)
        caps = [e for e in bm.edges if e.is_boundary]
        close_cut(bm, caps)

        # The detached tab is the connected component above the tailoring cut.
        top = max(bm.verts, key=lambda v: v.co.z)
        badge = {top}
        work = [top]
        while work:
            for edge in work.pop().link_edges:
                for v in edge.verts:
                    if v not in badge:
                        badge.add(v)
                        work.append(v)
        cloth = [v for v in bm.verts if v not in badge]
        if not 20 < len(badge) < len(cloth) // 4:
            raise ValueError(f'Unexpected original buckle geometry in {obj.name}.')

        # Raise the cloth around the former lug and narrow the long trapezoid.
        # The taper is stronger toward the ankle so the hip retains coverage.
        new_top = .026
        hem_z = -.873
        for v in cloth:
            depth = min(1., max(0., (cloth_top - v.co.z) / (cloth_top - hem_z)))
            v.co.x *= .94 + (final_width / initial_width - .94) * depth
            v.co.z += (new_top - cloth_top) * (1. - depth)

        # Lay the original embossed, textured buckle patch on the actual
        # outside cloth face. A ray hits that face first in panel-local -Y.
        cloth_faces = [f for f in bm.faces if f.verts[0] not in badge]
        local_index = {v: i for i, v in enumerate(cloth)}
        surface = BVHTree.FromPolygons(
            [v.co.copy() for v in cloth],
            [[local_index[v] for v in face.verts] for face in cloth_faces],
        )
        tab_top = max(v.co.z for v in badge)
        tab_back = max(v.co.y for v in badge)
        for v in badge:
            v.co.x *= .87
            v.co.z = new_top - .010 + .86 * (v.co.z - tab_top)
            hit, _, _, _ = surface.ray_cast((v.co.x, -.3, v.co.z), (0, 1, 0))
            if hit is None:
                raise RuntimeError(f'Buckle fell outside the cloth in {obj.name}.')
            # Embed its back 1 mm under the face; preserve embossed relief.
            v.co.y = hit.y + .001 - .31 * (tab_back - v.co.y)

        # Keep the original rig and its animated knee/ankle clearance. Reduce
        # only the sewn-in flare and curve the hem around the near leg.
        final_pitch = math.radians(-8 if is_front else 8)
        final_rot = Matrix.Rotation(final_pitch, 3, 'X') @ Matrix.Rotation(yaw, 3, 'Z')
        for v in bm.verts:
            depth = min(1., max(0., (new_top - v.co.z) / (new_top - hem_z)))
            v.co.y += depth * v.co.x * v.co.x / .80
            v.co = final_rot @ v.co + mount
        bm.normal_update()
        bm.to_mesh(mesh)
    finally:
        bm.free()
    mesh.update()


def fit_motion(scene):
    """Keep the front knee clear and release the rear panel over a trailing heel."""
    rig = scene.objects['JointedDollRig']
    for side in ('L', 'R'):
        mesh = scene.objects['front_panel.' + side].data
        for vertex in mesh.vertices:
            band = max(0., 1. - ((vertex.co.z - .645) / .23) ** 2)
            vertex.co.y -= .0045 * band ** 2
        mesh.update()

    old_action = rig.animation_data.action
    old_frame = scene.frame_current
    walk = bpy.data.actions['walk']
    try:
        rig.animation_data.action = walk
        # The original walk keys every frame. Its ankle reaches 145 mm behind
        # rest; the low outer corner then crosses the back of the actual shoe.
        # Release only the trailing panel, gradually from 18 mm travel onward.
        for frame in range(int(walk.frame_range[0]), int(walk.frame_range[1]) + 1):
            scene.frame_set(frame)
            for side in ('L', 'R'):
                foot = rig.pose.bones['foot.' + side]
                travel = foot.head.y - foot.bone.head_local.y
                fraction = max(0., min(1., (travel - .018) / (.145 - .018)))
                if fraction == 0.:
                    continue
                panel = rig.pose.bones['rear_panel.' + side]
                rest = panel.bone.matrix_local.to_quaternion()
                release = Quaternion((1, 0, 0), math.radians(7. * fraction))
                panel.rotation_quaternion = panel.rotation_quaternion @ (rest.inverted() @ release @ rest)
                panel.keyframe_insert(data_path='rotation_quaternion', frame=frame,
                                      group=panel.name, options={'INSERTKEY_REPLACE'})
    finally:
        rig.animation_data.action = old_action
        scene.frame_set(old_frame)
    bpy.context.view_layer.update()


def apply(scene):
    for prefix in PANELS:
        for side in ('L', 'R'):
            fit_panel(scene, prefix, side)
    fit_motion(scene)
