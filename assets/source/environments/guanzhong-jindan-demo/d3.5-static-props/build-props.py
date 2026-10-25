"""D3.5 authored static state library. Run only through blender_lab."""
import bpy
import bmesh
import json
import math
import random
import hashlib
from pathlib import Path
from mathutils import Vector

ROOT=Path(r'D:\天章游戏开发')
OUT=ROOT/'assets/source/environments/guanzhong-jindan-demo/d3.5-static-props'
UNITY=ROOT/'src/Assets/Art/Environments/Guanzhong/Props'
assert not (OUT/'StaticProps.blend').exists()
assert not (UNITY/'LowWallIntact.fbx').exists()
OUT.mkdir(parents=True,exist_ok=True)
UNITY.mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.new('D35_StaticProps_Authored')
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
random.seed(35025)
materials={n:bpy.data.materials.new(n) for n in ('WeatheredStone','StoneEdge','DarkStone','SealMetal','SealGlow','Leaf','LeafTip','DryStem','Charcoal','Ash')}
objects=[]

class Model:
    def __init__(self,name):
        self.name=name
        self.v=[]
        self.f=[]
        self.m=[]
    def geometry(self,verts,faces,material):
        offset=len(self.v)
        self.v.extend(verts)
        self.f.extend([[offset+i for i in face] for face in faces])
        self.m.extend([material]*len(faces))
    def stone(self,center,size,yaw=0,material='WeatheredStone',chipped=True):
        # Eight-sided chamfered cross section gives readable worn edges without tiny bevel meshes.
        w,h,d=size
        bevel=min(w,d)*.13
        ring=[(-w/2+bevel,-d/2),(w/2-bevel,-d/2),(w/2,-d/2+bevel),(w/2,d/2-bevel),
              (w/2-bevel,d/2),(-w/2+bevel,d/2),(-w/2,d/2-bevel),(-w/2,-d/2+bevel)]
        vertices=[]
        for level in (0,1):
            for x,z in ring:
                jitter=random.uniform(-.022,.022)*h if chipped and level else 0
                xx=x*math.cos(yaw)-z*math.sin(yaw)
                zz=x*math.sin(yaw)+z*math.cos(yaw)
                vertices.append((xx+center[0],center[1]+h*level+jitter,zz+center[2]))
        faces=[list(range(7,-1,-1)),list(range(8,16))]
        faces += [[i,(i+1)%8,(i+1)%8+8,i+8] for i in range(8)]
        self.geometry(vertices,faces,material)
    def rod(self,a,b,radius,material):
        a,b=Vector(a),Vector(b)
        axis=(b-a).normalized()
        side=axis.cross(Vector((0,0,1)))
        if side.length<.01: side=axis.cross(Vector((1,0,0)))
        side.normalize()
        other=axis.cross(side).normalized()
        vertices=[tuple(p+(side*math.cos(k*math.pi/3)+other*math.sin(k*math.pi/3))*radius)
                  for p in (a,b) for k in range(6)]
        faces=[list(range(5,-1,-1)),list(range(6,12))]+[[k,(k+1)%6,(k+1)%6+6,k+6] for k in range(6)]
        self.geometry(vertices,faces,material)
    def finish(self):
        mesh=bpy.data.meshes.new(self.name+'_AuthoredMesh')
        mesh.from_pydata([(-x,-z,y) for x,y,z in self.v],[],[list(reversed(f)) for f in self.f])
        keys=list(materials)
        for mat in materials.values(): mesh.materials.append(mat)
        for face,mat in zip(mesh.polygons,self.m): face.material_index=keys.index(mat)
        bm=bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        uv=mesh.uv_layers.new(name='UVMap')
        for poly in mesh.polygons:
            normal=poly.normal
            for loop in poly.loop_indices:
                p=mesh.vertices[mesh.loops[loop].vertex_index].co
                # Reuse the original Stair stone atlas area, with metre-oriented planar projections.
                u,v=(p.x,p.y) if abs(normal.z)>.7 else ((p.x,p.z) if abs(normal.y)>.7 else (p.y,p.z))
                uv.data[loop].uv=(.29+u*.22,.36+v*.22)
        mesh.update()
        obj=bpy.data.objects.new(self.name,mesh)
        scene.collection.objects.link(obj)
        objects.append(obj)
        return obj

for tall in (False,True):
    for damaged in (False,True):
        name=('HighWall' if tall else 'LowWall')+('Damaged' if damaged else 'Intact')
        model=Model(name)
        rows=8 if tall else 3
        length=.57 if tall else .61
        for row in range(rows):
            count=3 if row%2==0 else 2
            for col in range(count):
                if damaged and row>=(3 if tall else 1) and col==count-1: continue
                if damaged and row>=(5 if tall else 2) and col==1: continue
                width=length/count
                x=-length/2+(col+.5)*width
                model.stone((x,row*.125,0),(width-.007,.122,.16),
                    material='StoneEdge' if row==rows-1 else 'WeatheredStone')
        model.finish()

rubble=Model('SharedRubble')
for x,z,w,d,h,yaw in [(-.20,0,.17,.13,.10,.3),(-.035,.025,.19,.14,.075,-.4),(.17,-.015,.14,.12,.065,.6),
                       (-.12,-.08,.09,.07,.04,.9),(.07,-.075,.07,.065,.035,-.2),(.025,.09,.08,.06,.04,1.0)]:
    rubble.stone((x,0,z),(w,h,d),yaw,'WeatheredStone')
rubble.finish()

stele=Model('SteleIntact')
stele.stone((0,0,0),(.26,.08,.19),material='DarkStone',chipped=False)
stele.stone((0,.08,0),(.19,.56,.12),material='WeatheredStone',chipped=False)
stele.stone((0,.64,0),(.23,.07,.15),material='StoneEdge',chipped=False)
stele.stone((0,.71,0),(.16,.045,.12),material='StoneEdge',chipped=False)
for height in (.105,.59): stele.stone((0,height,-.064),(.17,.012,.012),material='SealMetal',chipped=False)
# A recessed-looking dark seal panel separates the on/off light from the body silhouette.
stele.stone((0,.24,-.062),(.115,.29,.008),material='DarkStone',chipped=False)
stele.finish()
glow=Model('SteleGlow')
for z in (-.069,.063):
    for a,b in [((0,.29,z),(0,.50,z)),((0,.51,z),(.04,.435,z)),((.04,.435,z),(0,.36,z)),
                ((0,.36,z),(-.04,.435,z)),((-.04,.435,z),(0,.51,z)),((-.045,.32,z),(.045,.32,z))]:
        glow.rod(a,b,.006,'SealGlow')
glow.finish()
failed=Model('SteleFailed')
failed.stone((0,0,0),(.26,.08,.19),material='DarkStone')
failed.stone((-.025,.08,0),(.14,.26,.12),material='WeatheredStone')
failed.stone((.07,.08,-.015),(.09,.16,.13),material='StoneEdge')
failed.stone((.065,.015,-.075),(.16,.065,.085),.35,'StoneEdge')
failed.finish()

for burnt in (False,True):
    plant=Model('LowPlantCharred' if burnt else 'LowPlantLiving')
    rng=random.Random(103)
    for i in range(17):
        angle=i*2.399
        radius=.025+.055*rng.random()
        base=(math.cos(angle)*radius,0,math.sin(angle)*radius)
        height=(.09+.15*rng.random())*(.64 if burnt else 1)
        tip=(base[0]+math.cos(angle)*.04,height,base[2]+math.sin(angle)*.04)
        plant.rod(base,tip,.0045,'Charcoal' if burnt else 'DryStem')
        if not burnt:
            for t in (.42,.72,1):
                p=Vector(base).lerp(Vector(tip),t)
                d=Vector((math.cos(angle+t*4),.28,math.sin(angle+t*4)))
                side=Vector((-d.z,0,d.x))*.017
                end=p+d*(.055 if t<1 else .04)
                mid=p.lerp(end,.48)+Vector((0,.01,0))
                # Folded double-sided leaves, authored geometry rather than alpha cards.
                points=[tuple(p),tuple(mid+side),tuple(end),tuple(mid-side),tuple(mid+Vector((0,.008,0)))]
                faces=[[0,1,4],[1,2,4],[2,3,4],[3,0,4]]
                plant.geometry(points,faces+[list(reversed(f)) for f in faces],'LeafTip' if i%3==0 else 'Leaf')
        elif i%4==0:
            plant.stone((base[0],0,base[2]),(.025,.006,.02),material='Ash')
    plant.finish()

blend=OUT/'StaticProps.blend'
bpy.data.libraries.write(str(blend),set(objects),fake_user=True,compress=True)
previous=bpy.context.window.scene
bpy.context.window.scene=scene
try:
    for obj in objects:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        scene.view_layers[0].objects.active=obj
        bpy.ops.export_scene.fbx(filepath=str(UNITY/(obj.name+'.fbx')),use_selection=True,
            object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,
            apply_unit_scale=True,bake_space_transform=True,bake_anim=False,path_mode='STRIP')
finally:
    bpy.context.window.scene=previous
proof={}
for obj in objects:
    obj.data.calc_loop_triangles()
    proof[obj.name]=dict(vertices=len(obj.data.vertices),triangles=len(obj.data.loop_triangles),
        unityMin=[min(-v.co.x for v in obj.data.vertices),min(v.co.z for v in obj.data.vertices),min(-v.co.y for v in obj.data.vertices)],
        unityMax=[max(-v.co.x for v in obj.data.vertices),max(v.co.z for v in obj.data.vertices),max(-v.co.y for v in obj.data.vertices)],
        fbxSha256=hashlib.sha256((UNITY/(obj.name+'.fbx')).read_bytes()).hexdigest())
result=dict(scope='Separate static mesh states only. No collision, fuel, durability, abilities or game state.',
    stoneTexture='Assets/Art/Environments/Guanzhong/Stair_basecolor.JPEG',
    stoneTextureSha256=hashlib.sha256((ROOT/'src/Assets/Art/Environments/Guanzhong/Stair_basecolor.JPEG').read_bytes()).hexdigest(),
    blendSha256=hashlib.sha256(blend.read_bytes()).hexdigest(),assets=proof)
(OUT/'source-proof.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
