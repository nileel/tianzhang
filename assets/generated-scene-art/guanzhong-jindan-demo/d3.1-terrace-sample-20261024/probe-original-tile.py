# Run with blender_lab.execute_blender_code. Read-only original mesh inspection.
# The source is the preserved D1 imported Tile; validate its FBX hash first.
import bpy
import collections
import hashlib
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree

root = Path(r'D:\天章游戏开发')
output = root / 'assets/generated-scene-art/guanzhong-jindan-demo/d3.1-terrace-sample-20261024'
source = root / 'src/Assets/Art/Environments/Guanzhong/Tile.fbx'
assert hashlib.sha256(source.read_bytes()).hexdigest() == '504b1e7b390d5c061d911a3787d2171a241c4c6ce2cb58479aa53dbb899ffbee'
imports = json.loads(Path(r'D:\Temp\TianZhang-Blender\d1-measure-20261024\01-import-state.json').read_text(encoding='utf-8'))
tile = next(o for o in bpy.data.scenes['D1_Measure_20261024'].objects if o.type == 'MESH' and o.name.startswith('D1_Tile_'))
matrix = Matrix(next(e for e in imports['Tile']['objects'] if e['type'] == 'MESH')['matrix_world'])
rotation = Quaternion((.1757585685908413, -.17575856859084127, .6849152688960135, .6849152688960136))
position = Vector((.24881442194815115, -.8945829622159506, -.4738227693251703))
vertices = []
for vertex in tile.data.vertices:
    b = matrix @ vertex.co
    vertices.append(rotation @ Vector((-b.x, b.z, -b.y)) * 1.9770544758933135 + position)
faces = [list(p.vertices) for p in tile.data.polygons]
bvh = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
edges = collections.Counter(tuple(sorted((f[i], f[(i + 1) % len(f)]))) for f in faces for i in range(len(f)))
rows = []
for direction in range(6):
    angle = math.radians(direction * 60)
    normal = Vector((math.cos(angle), 0, math.sin(angle)))
    tangent = Vector((-math.sin(angle), 0, math.cos(angle)))
    gaps = []
    for k in range(101):
        lateral = (k / 100 - .5) / math.sqrt(3)
        for offset in [-.02, -.01, 0, .01, .02]:
            p = normal * (.5 + offset) + tangent * lateral
            tops = []
            for center in [Vector((0, 0, 0)), normal]:
                hit = bvh.ray_cast(p - center + Vector((0, 4, 0)), Vector((0, -1, 0)))[0]
                tops.append(hit.y if hit else -99)
            if max(tops) < -.025:
                gaps.append([lateral, offset, tops])
    wall = []
    for rise in [.75, 1.5]:
        misses, deep_recesses, distances = [], [], []
        for k in range(61):
            lateral = (k / 60 - .5) / math.sqrt(3)
            for j in range(30):
                height = .04 + (rise - .08) * j / 29
                origin = normal * .8 + tangent * lateral + Vector((0, height - rise, 0))
                hit, _, _, distance = bvh.ray_cast(origin, -normal, 1.3)
                if hit is None:
                    misses.append([lateral, height])
                else:
                    distances.append(distance)
                    if distance > .5:
                        deep_recesses.append([lateral, height, distance])
        wall.append({'rise': rise, 'rays': 1830, 'miss': len(misses),
                     'recessBeyondRadius0_3': len(deep_recesses),
                     'maxRayDistance': max(distances), 'examples': deep_recesses[:3]})
    rows.append({'directionDegrees': direction * 60, 'seamStripRays': 505,
                 'seamBelowMinus25mm': len(gaps), 'seamExamples': gaps[:3], 'wall': wall})
result = {
    'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'sourceUntouched': True, 'vertices': len(vertices), 'triangles': len(faces),
    'boundaryEdges': sum(count == 1 for count in edges.values()),
    'nonManifoldEdges': sum(count != 2 for count in edges.values()),
    'irregularEdges': [{'vertices': list(edge), 'uses': count,
                       'lengthMeters': (vertices[edge[0]] - vertices[edge[1]]).length}
                      for edge, count in edges.items() if count != 2],
    'rootCenterY': bvh.ray_cast(Vector((0, 4, 0)), Vector((0, -1, 0)))[0].y,
    'localBoundsMin': [min(v[i] for v in vertices) for i in range(3)],
    'localBoundsMax': [max(v[i] for v in vertices) for i in range(3)],
    'sixDirections': rows,
    'limits': 'Finite sample, not a continuous watertight proof. Side rays include stone recesses; a distant hit alone does not prove a flush side wall. Unity views decide visible gaps. No rule/collider changes.'
}
(output / 'original-geometry-probe.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
