"""Run through blender_lab: remesh the old Stair's stone appearance to the frozen D1 profile."""
import bpy
import bmesh
import collections
import csv
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(r'D:\天章游戏开发')
OUT = ROOT / 'assets/source/environments/guanzhong-jindan-demo/d3.3-stone-stairs'
UNITY = ROOT / 'src/Assets/Art/Environments/Guanzhong/Stairs'
SOURCE = ROOT / 'src/Assets/Art/Environments/Guanzhong/Stair.fbx'
TEXTURE = ROOT / 'src/Assets/Art/Environments/Guanzhong/Stair_basecolor.JPEG'
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == 'd1e5a51e8d934a827e90f2e77376c8f0697be6999f1c32407aeb41bee2c9eda9'
assert hashlib.sha256(TEXTURE.read_bytes()).hexdigest() == '23e14161e0051843fedfd8db1b691587a9638c94c4966e4f652d3b6549fcc54a'
assert not (OUT / 'StoneStair3.blend').exists() and not (UNITY / 'StoneStair3.fbx').exists()
with (ROOT / 'src/Assets/DataConfig/EnvironmentProfiles.csv').open(encoding='utf-8-sig', newline='') as f:
    row = next(r for r in csv.DictReader(f) if r['profileId'] == 'env_guanzhong_jindan_demo')
profile = json.loads(row['physicalSurfaceProfiles'])['stairConnections'][0]
origin = (profile['platformCenters'][0]['worldX'], profile['lowerY'], profile['worldZStart'])
risers = [r['worldZ'] - origin[2] for r in profile['risers']]
cuts = [-10] + risers + [10]
heights = [0] + [r['supportYAfter'] - origin[1] for r in profile['risers']]
bottom = -1.88311052

def clip(poly, z, above):
    out = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        ina, inb = (a[1] >= z) == above, (b[1] >= z) == above
        if ina:
            out.append(a)
        if ina != inb:
            t = (z-a[1])/(b[1]-a[1])
            out.append((a[0]+t*(b[0]-a[0]), z))
    return out

patches = []
for cell in profile['coveredCells']:
    q, r = map(int, cell.split(':'))
    cx, cz = q+.5*r-origin[0], 1+math.sqrt(3)*.5*r-origin[2]
    hexagon = [(cx+math.cos(math.pi/6+k*math.pi/3)/math.sqrt(3),
                cz+math.sin(math.pi/6+k*math.pi/3)/math.sqrt(3)) for k in range(6)]
    for i, y in enumerate(heights):
        poly = clip(clip(hexagon, cuts[i], True), cuts[i+1], False)
        if len(poly) >= 3:
            patches.append(([(round(x,8),round(z,8)) for x,z in poly], round(y,8), i))

vertices, faces, mats, uv_faces, index = [], [], [], [], {}
def vertex(x, y, z):
    # Match the D3.2 metre/axis convention: Unity importer mirrors X.
    key = (round(-x,8), round(-z,8), round(y,8))
    if key not in index:
        index[key] = len(vertices)
        vertices.append(key)
    return index[key]

def face(points, material, tex):
    faces.append([vertex(*p) for p in reversed(points)])
    mats.append(material)
    uv_faces.append(list(reversed(tex)))

edges = collections.defaultdict(list)
levels = collections.defaultdict(set)
for poly, y, band in patches:
    # Original Stair atlas supplies the blue-grey stone and cracks; no replacement texture.
    face([(x,y,z) for x,z in reversed(poly)], 0,
         [(.27+(x+.5)*.105, .30+(z+.58)*.12) for x,z in reversed(poly)])
    face([(x,bottom,z) for x,z in poly], 1, [(x*.1+.5,z*.1+.5) for x,z in poly])
    for a,b in zip(poly,poly[1:]+poly[:1]):
        edges[tuple(sorted((a,b)))].append((a,b,y))
        levels[a].update((bottom,y))
        levels[b].update((bottom,y))

for key, rows in edges.items():
    assert len(rows) <= 2
    if len(rows)==2 and rows[0][2]==rows[1][2]:
        continue
    a,b,high = max(rows,key=lambda t:t[2])
    low = bottom if len(rows)==1 else min(r[2] for r in rows)
    # Include every vertical split at a rim vertex so adjacent walls share real edges.
    aa = sorted(y for y in levels[a] if low <= y <= high)
    bb = sorted((y for y in levels[b] if low <= y <= high), reverse=True)
    pts = [(a[0],y,a[1]) for y in aa] + [(b[0],y,b[1]) for y in bb]
    material = 2 if len(rows)==2 else 1
    face(pts,material,[(.28+(x+.5)*.105,.32+(y-low)*.12) for x,y,z in pts])

scene = bpy.data.scenes.new('D33_StoneStair3_Authored')
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
mesh = bpy.data.meshes.new('StoneStair3_TrueTreadsAndSolidSides')
mesh.from_pydata(vertices,[],faces)
mesh.update()
for name in ['StairTread','StairWall','StairRiser']:
    mesh.materials.append(bpy.data.materials.new(name))
uv = mesh.uv_layers.new(name='UVMap')
for p,mat,coords in zip(mesh.polygons,mats,uv_faces):
    p.material_index = mat
    for loop,co in zip(p.loop_indices,coords):
        uv.data[loop].uv = co
bm = bmesh.new()
bm.from_mesh(mesh)
assert all(e.is_manifold for e in bm.edges), 'Stair must be a closed welded solid.'
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new('StoneStair3',mesh)
scene.collection.objects.link(obj)
OUT.mkdir(parents=True,exist_ok=True)
UNITY.mkdir(parents=True,exist_ok=True)
blend,fbx = OUT/'StoneStair3.blend', UNITY/'StoneStair3.fbx'
bpy.data.libraries.write(str(blend),{obj},fake_user=True,compress=True)
previous = bpy.context.window.scene
bpy.context.window.scene = scene
obj.select_set(True)
scene.view_layers[0].objects.active = obj
try:
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},
        axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,
        bake_space_transform=True,bake_anim=False,path_mode='STRIP')
finally:
    bpy.context.window.scene = previous
mesh.calc_loop_triangles()
proof = dict(mapVersion=row['mapVersion'], profile=profile, localOriginUnity=origin,
    vertices=len(mesh.vertices),triangles=len(mesh.loop_triangles),nonManifoldEdges=0,
    originalStairSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    originalTextureSha256=hashlib.sha256(TEXTURE.read_bytes()).hexdigest(),
    blendSha256=hashlib.sha256(blend.read_bytes()).hexdigest(),
    fbxSha256=hashlib.sha256(fbx.read_bytes()).hexdigest(),
    reuse='Original Stair stone atlas and appearance; dimensioned remesh replaces its undersized treads. Original FBX and prefab unchanged.',
    nominalMiddlePlatformCoreDepth=.5,actualMiddleHorizontalDepth=risers[4]-risers[3])
(OUT/'source-proof.json').write_text(json.dumps(proof,indent=2),encoding='utf-8')
result = {k:v for k,v in proof.items() if k!='profile'}
