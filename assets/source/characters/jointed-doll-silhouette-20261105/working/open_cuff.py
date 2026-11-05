"""Repair tiny raw seams, then open the generated seal around the real forearm."""
import bpy
import bmesh
import math


def clean(scene, raw):
    obj = raw.copy()
    obj.data = raw.data.copy()
    obj.name = 'LongSleeveManifold'
    scene.collection.objects.link(obj)
    obj.hide_render = True
    obj.hide_set(True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    # FBX mesh coordinates are centimetres; .01 is 0.1 mm in world space.
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.01)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


def open_passage(scene):
    """Cut the false interior seal while retaining the generated exterior."""
    for side, sign in [('L', 1), ('R', -1)]:
        # Temporary cutting volume ends at the fingers; an infinite bore
        # would unnecessarily cut the visible lining below the hand.
        rings = [(.35, .073, .065, -.026), (.69, .073, .065, -.026),
                 (.74, .073, .050, -.036), (.81, .073, .050, -.036),
                 (.87, .055, .030, -.026), (.90, .005, .005, -.026)]
        count = 96
        vertices = [(sign * x, centre_y + radius_y * math.cos(2 * math.pi * i / count),
                     1.360 - .070 * (x - .447) + radius_z * math.sin(2 * math.pi * i / count))
                    for x, radius_y, radius_z, centre_y in rings for i in range(count)]
        faces = []
        for ring in range(len(rings) - 1):
            for i in range(count):
                j = (i + 1) % count
                faces.append((ring * count + i, ring * count + j,
                              (ring + 1) * count + j, (ring + 1) * count + i))
        faces += [tuple(reversed(range(count))), tuple(range((len(rings) - 1) * count, len(vertices)))]
        mesh = bpy.data.meshes.new('TemporaryHandClearance')
        mesh.from_pydata(vertices, [], faces)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        cutter = bpy.data.objects.new('TemporaryHandClearance', mesh)
        scene.collection.objects.link(cutter)
        target = scene.objects['sleeve.' + side]
        modifier = target.modifiers.new('OpenGeneratedInnerSeal', 'BOOLEAN')
        modifier.operation = 'DIFFERENCE'
        modifier.solver = 'EXACT'
        modifier.object = cutter
        bpy.ops.object.select_all(action='DESELECT')
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.modifier_move_up(modifier=modifier.name)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.data.objects.remove(cutter, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        forearm = target.vertex_groups['forearm.' + side]
        hanging = target.vertex_groups['sleeve.' + side]
        for vertex in target.data.vertices:
            weight = max(0., min(1., (abs(vertex.co.x) - .84) / .21))
            weight = weight * weight * (3 - 2 * weight)
            forearm.add([vertex.index], 1. - weight, 'REPLACE')
            hanging.add([vertex.index], weight, 'REPLACE')
