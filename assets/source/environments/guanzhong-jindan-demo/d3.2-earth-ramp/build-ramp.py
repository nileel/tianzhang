"""Run in blender_lab. Author the D1 finite 24-hex ramp; preserve all existing scenes."""
import bpy
import collections
import csv
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(r'D:\天章游戏开发')
OUT = ROOT / 'assets/source/environments/guanzhong-jindan-demo/d3.2-earth-ramp'
UNITY = ROOT / 'src/Assets/Art/Environments/Guanzhong/Ramp'
SCENE = 'D32_EarthRamp_Source'
if (OUT / 'source-proof.json').exists():
    previous = json.loads((OUT / 'source-proof.json').read_text())
    for path, key in [(OUT / 'EarthRamp24.blend', 'blendSha256'), (UNITY / 'EarthRamp24.fbx', 'fbxSha256')]:
        assert hashlib.sha256(path.read_bytes()).hexdigest() == previous[key], f'Unrecorded edit: {path}'
if SCENE in bpy.data.scenes:
    old = bpy.data.scenes[SCENE]
    assert [o.name for o in old.objects] == ['EarthRamp24']
    bpy.data.objects.remove(old.objects[0], do_unlink=True)
    bpy.data.scenes.remove(old)
with (ROOT / 'src/Assets/DataConfig/EnvironmentProfiles.csv').open(encoding='utf-8-sig', newline='') as stream:
    row = next(r for r in csv.DictReader(stream) if r['profileId'] == 'env_guanzhong_jindan_demo')
data = json.loads(row['physicalSurfaceProfiles'])
profile = next(p for p in data['rampProfiles'] if p['id'] == 'left_ramp')
cells = [tuple(map(int, s.split(':'))) for s in profile['coveredCells']]
shoulders = set(profile['shoulderCells'])
origin = (0.5, profile['lowerY'], profile['worldZStart'])
length = profile['worldZEnd'] - profile['worldZStart']
rise = profile['upperY'] - profile['lowerY']
assert len(cells) == 24 and abs(length - 5 * math.sqrt(3) / 2) < 1e-8
N = 12
bottom = -1.88311052

def height(z):
    return rise * .5 * (1 - math.cos(math.pi * max(0, min(1, z / length))))

vertices, faces, materials, uvs = [], [], [], []
index = {}

def vertex(x, y, z):
    # Unity FBX import mirrors X. Author the inverse and reverse the face winding.
    key = (round(-x, 8), round(-z, 8), round(y, 8))
    if key not in index:
        index[key] = len(vertices)
        vertices.append(key)
    return index[key]

def face(points, mat, tex):
    ids = [vertex(*p) for p in reversed(points)]
    faces.append(ids)
    materials.append(mat)
    uvs.append(list(reversed(tex)))

edges = collections.Counter()
edge_points = {}
for q, r in cells:
    cx, cz = q + .5 * r - origin[0], 1 + math.sqrt(3) / 2 * r - origin[2]
    corners = [(cx + math.cos(math.pi / 6 + k * math.pi / 3) / math.sqrt(3),
                cz + math.sin(math.pi / 6 + k * math.pi / 3) / math.sqrt(3)) for k in range(6)]
    mat = 1 if f'{q}:{r}' in shoulders else 0
    for k, a in enumerate(corners):
        b = corners[(k + 1) % 6]
        edge = tuple(sorted((tuple(round(v, 8) for v in a), tuple(round(v, 8) for v in b))))
        edges[edge] += 1
        edge_points[edge] = (a, b)
        def point(i, j):
            x = cx + ((a[0] - cx) * i + (b[0] - cx) * j) / N
            z = cz + ((a[1] - cz) * i + (b[1] - cz) * j) / N
            return (x, height(z), z)
        for i in range(N):
            for j in range(N - i):
                p = [point(i, j), point(i, j + 1), point(i + 1, j)]
                face(p, mat, [(v[0], v[2]) for v in p])
                if i + j < N - 1:
                    p = [point(i + 1, j), point(i, j + 1), point(i + 1, j + 1)]
                    face(p, mat, [(v[0], v[2]) for v in p])
        # Matching tessellation on the bottom keeps the complete solid welded/manifold.
        for i in range(N):
            for j in range(N - i):
                p = [point(i, j), point(i + 1, j), point(i, j + 1)]
                p = [(x, bottom, z) for x, y, z in p]
                face(p, 2, [(x * .1 + .5, z * .1 + .5) for x, y, z in p])
                if i + j < N - 1:
                    p = [point(i + 1, j), point(i + 1, j + 1), point(i, j + 1)]
                    p = [(x, bottom, z) for x, y, z in p]
                    face(p, 2, [(x * .1 + .5, z * .1 + .5) for x, y, z in p])

for edge, count in edges.items():
    if count != 1:
        continue
    a, b = edge_points[edge]
    for i in range(N):
        t0, t1 = i / N, (i + 1) / N
        x0, z0 = a[0] * (1-t0) + b[0]*t0, a[1]*(1-t0)+b[1]*t0
        x1, z1 = a[0] * (1-t1) + b[0]*t1, a[1]*(1-t1)+b[1]*t1
        # Top rim and bottom are exact; the rock texture provides detail without protrusions.
        p = [(x0, height(z0), z0), (x1, height(z1), z1), (x1, bottom, z1), (x0, bottom, z0)]
        face(p, 2, [(t0*.23+.50, .80), (t1*.23+.50, .80), (t1*.23+.50, .17), (t0*.23+.50, .17)])

scene = bpy.data.scenes.new(SCENE)
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
mesh = bpy.data.meshes.new('EarthRamp24_ContinuousSolid')
mesh.from_pydata(vertices, [], faces)
mesh.update()
obj = bpy.data.objects.new('EarthRamp24', mesh)
scene.collection.objects.link(obj)
for name, color in [('RampEarth', (.39,.31,.22,1)), ('RampShoulder', (.28,.31,.30,1)), ('RampRock', (.29,.38,.40,1))]:
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = color
    mesh.materials.append(material)
uv = mesh.uv_layers.new(name='UVMap')
for poly, mat, coords in zip(mesh.polygons, materials, uvs):
    poly.material_index = mat
    poly.use_smooth = mat != 2
    for loop, coord in zip(poly.loop_indices, coords):
        uv.data[loop].uv = coord

OUT.mkdir(parents=True, exist_ok=True)
UNITY.mkdir(parents=True, exist_ok=True)
blend = OUT / 'EarthRamp24.blend'
fbx = UNITY / 'EarthRamp24.fbx'
# Object library keeps only this asset and dependencies. Blender 5.2's scene-library
# writer crashes in BKE_view_layer_copy_data; this asset needs no copied view layer.
bpy.data.libraries.write(str(blend), {obj}, fake_user=True, compress=True)
old_scene = bpy.context.window.scene
bpy.context.window.scene = scene
obj.select_set(True)
scene.view_layers[0].objects.active = obj
try:
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH'},
        axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
        bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False,
        bake_anim=False, path_mode='STRIP', use_custom_props=False)
finally:
    bpy.context.window.scene = old_scene
mesh.calc_loop_triangles()
edge_count = collections.Counter(tuple(sorted((f[i], f[(i+1)%len(f)]))) for f in faces for i in range(len(f)))
assert all(c == 2 for c in edge_count.values()), 'The ramp must be a closed manifold solid.'
proof = {'profile':profile, 'mapVersion':row['mapVersion'], 'localOriginUnity':origin,
    'localCoordinates':'x = worldX - .5; y = worldY - lowerY; z = worldZ - worldZStart',
    'vertices':len(vertices), 'triangles':len(mesh.loop_triangles), 'nonManifoldEdges':0,
    'subdivisionsPerHexRadius':N, 'maxTopInterpolationErrorBoundMeters':rise*math.pi**2/(2*length**2)*(1/math.sqrt(3)/N)**2/8,
    'bottomY':bottom, 'blendSha256':hashlib.sha256(blend.read_bytes()).hexdigest(),
    'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest()}
(OUT / 'source-proof.json').write_text(json.dumps(proof, indent=2), encoding='utf-8')
result = {k:v for k,v in proof.items() if k != 'profile'}
