"""D3.2 port derivative: retain original rock below the rim; replace crown by one flat cap."""
import bpy
import bmesh
import hashlib
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

ROOT = Path(r'D:\天章游戏开发')
OUT = ROOT / 'assets/source/environments/guanzhong-jindan-demo/d3.2-earth-ramp'
UNITY = ROOT / 'src/Assets/Art/Environments/Guanzhong/Ramp'
SCENE = 'D32_RampLanding_Source'
assert SCENE not in bpy.data.scenes
tile_path = ROOT / 'src/Assets/Art/Environments/Guanzhong/Tile.fbx'
assert hashlib.sha256(tile_path.read_bytes()).hexdigest() == '504b1e7b390d5c061d911a3787d2171a241c4c6ce2cb58479aa53dbb899ffbee'
tile = next(o for o in bpy.data.scenes['D1_Measure_20261024'].objects if o.type == 'MESH' and o.name.startswith('D1_Tile_'))
imports = json.loads((ROOT / 'docs/基础设定/演武地图/关中阶地-20261024/geometry/01-import-state.json').read_text(encoding='utf-8'))
matrix = Matrix(next(e for e in imports['Tile']['objects'] if e['type'] == 'MESH')['matrix_world'])
rotation = Quaternion((.1757585685908413, -.17575856859084127, .6849152688960135, .6849152688960136))
position = Vector((.24881442194815115, -.8945829622159506, -.4738227693251703))
mesh = tile.data.copy()
mesh.name = 'RampLanding_OriginalRock'
for vertex in mesh.vertices:
    b = matrix @ vertex.co
    u = rotation @ Vector((-b.x, b.z, -b.y)) * 1.9770544758933135 + position
    vertex.co = Vector((-u.x, -u.z, u.y))
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
    dist=1e-7, plane_co=(0,0,-.10), plane_no=(0,0,1), clear_outer=True)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
bm.to_mesh(mesh)
bm.free()
mesh.materials.clear()
rock = bpy.data.materials.get('RampRock')
assert rock
mesh.materials.append(rock)
for face in mesh.polygons:
    face.material_index = 0
scene = bpy.data.scenes.new(SCENE)
scene.unit_settings.system = 'METRIC'
base = bpy.data.objects.new('LandingOriginalRock',mesh)
scene.collection.objects.link(base)

# The cap replaces (never stacks over) the old horizontal crown. Its lower collar overlaps
# the retained rock inside the solid, so no grass/old rim can become another support surface.
top = .000433121
verts = [(0,0,top),(0,0,-.25)]
for k in range(6):
    angle = math.pi/6+k*math.pi/3
    x,z = math.cos(angle)/math.sqrt(3),math.sin(angle)/math.sqrt(3)
    verts.extend([(-x,-z,top),(-x,-z,-.25)])
faces,uvs,mats=[],[],[]
for k in range(6):
    a,b=2+k*2,2+((k+1)%6)*2
    for ids,mat in [([0,a,b],0),([1,b+1,a+1],1),([a,a+1,b+1,b],1)]:
        faces.append(ids)
        uvs.append([(-verts[i][0],-verts[i][1]) if mat==0 else (.55 + (-verts[i][0]+.5)*.18,.45+verts[i][2]) for i in ids])
        mats.append(mat)
capmesh=bpy.data.meshes.new('RampLanding_ExactFlatHexCap')
capmesh.from_pydata(verts,[],faces)
capmesh.update()
capmesh.materials.append(bpy.data.materials.get('RampLandingTop') or bpy.data.materials.new('RampLandingTop'))
capmesh.materials.append(rock)
uv=capmesh.uv_layers.new(name='UVMap')
for poly,coords,mat in zip(capmesh.polygons,uvs,mats):
    poly.material_index=mat
    for loop,coord in zip(poly.loop_indices,coords): uv.data[loop].uv=coord
cap=bpy.data.objects.new('LandingExactCap',capmesh)
scene.collection.objects.link(cap)
blend=OUT/'RampLanding.blend'
fbx=UNITY/'RampLanding.fbx'
assert not blend.exists() and not fbx.exists()
bpy.data.libraries.write(str(blend),{base,cap},fake_user=True,compress=True)
old_scene=bpy.context.window.scene
bpy.context.window.scene=scene
base.select_set(True)
cap.select_set(True)
scene.view_layers[0].objects.active=base
try:
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
        global_scale=1,apply_unit_scale=True,bake_space_transform=True,bake_anim=False,path_mode='STRIP')
finally:
    bpy.context.window.scene=old_scene
mesh.calc_loop_triangles()
capmesh.calc_loop_triangles()
result={'originalTileSha256':hashlib.sha256(tile_path.read_bytes()).hexdigest(),
    'triangles':len(mesh.loop_triangles)+len(capmesh.loop_triangles),
    'retainedOriginalRockTopLocalY':-.10,'replacementSupportLocalY':top,'collarBottomLocalY':-.25,
    'blendSha256':hashlib.sha256(blend.read_bytes()).hexdigest(),'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),
    'scope':'Only lower/upper ramp port platforms. Original Tile and D3.1 remain unchanged.'}
(OUT/'landing-proof.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
