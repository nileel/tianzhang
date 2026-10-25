"""Author D3.4 metre-sized visual sheets through blender_lab; no support/collision layer."""
import bpy
import json
import math
import hashlib
from pathlib import Path

ROOT = Path(r'D:\天章游戏开发')
OUT = ROOT / 'assets/source/environments/guanzhong-jindan-demo/d3.4-waterbank'
UNITY = ROOT / 'src/Assets/Art/Environments/Guanzhong/Waterbank'
assert not (OUT / 'Waterbank.blend').exists()
assert not (UNITY / 'WaterHex.fbx').exists()
scene = bpy.data.scenes.new('D34_Waterbank_Authored')
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
OUT.mkdir(parents=True, exist_ok=True)
UNITY.mkdir(parents=True, exist_ok=True)

def sheet(name, points, faces, material):
    mesh = bpy.data.meshes.new(name)
    # Same authored Blender -> imported Unity frame as D3.2/D3.3.
    mesh.from_pydata([(-x, -z, y) for x,y,z in points], [], [list(reversed(f)) for f in faces])
    mesh.materials.append(bpy.data.materials.new(material))
    uv = mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        for loop in poly.loop_indices:
            p = points[mesh.loops[loop].vertex_index]
            uv.data[loop].uv = (p[0], p[2])
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj

hexagon = [(math.cos(math.pi/6+k*math.pi/3)/math.sqrt(3), 0,
            math.sin(math.pi/6+k*math.pi/3)/math.sqrt(3)) for k in range(6)]
# Reverse the CCW XZ perimeter: positive Unity Y is the visible side.
water = sheet('WaterHex', [(0,0,0)]+hexagon, [[0,1+(k+1)%6,1+k] for k in range(6)], 'Water')
d = .5/math.sqrt(3)
shore = sheet('ShoreEdge', [(.5,0,-d),(.5,0,d),(.455,0,d*.91),(.455,0,-d*.91)],
              [[0,3,2,1]], 'WetShore')
objects = [water, shore]
bpy.data.libraries.write(str(OUT/'Waterbank.blend'), set(objects), fake_user=True, compress=True)
previous = bpy.context.window.scene
bpy.context.window.scene = scene
try:
    for obj in objects:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        scene.view_layers[0].objects.active = obj
        bpy.ops.export_scene.fbx(filepath=str(UNITY/(obj.name+'.fbx')), use_selection=True,
            object_types={'MESH'}, axis_forward='-Z', axis_up='Y', global_scale=1,
            apply_unit_scale=True, bake_space_transform=True, bake_anim=False, path_mode='STRIP')
finally:
    bpy.context.window.scene = previous
proof = dict(hexInradius=.5, hexCircumradius=1/math.sqrt(3), shoreWidth=.045,
    waterRenderOffset=.004, shoreRenderOffset=.005, newSupportLayer=False,
    scope='Static sheets only; existing RampLanding keeps the unique flat support. No water simulation.',
    assets={o.name:dict(vertices=len(o.data.vertices),
        fbxSha256=hashlib.sha256((UNITY/(o.name+'.fbx')).read_bytes()).hexdigest()) for o in objects})
(OUT/'source-proof.json').write_text(json.dumps(proof, indent=2), encoding='utf-8')
result = proof
