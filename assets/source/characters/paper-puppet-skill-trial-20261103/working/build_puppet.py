"""Build the specifically approved rigid puppet from the corrected Tripo mesh.
Run inside the connected Blender instance; source geometry is kept in its own Blend.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion
import numpy as np

ROOT = Path('D:/天章游戏开发/assets/source/characters/paper-puppet-skill-trial-20261103')
SOURCE = ROOT / 'working/fuyuan-skill-corrected-source.blend'
assert Path(bpy.data.filepath) == SOURCE, 'Open the source Blend in a separate tool call first.'
scene = bpy.context.scene
source = bpy.data.objects['FuYuan_Corrected_Source']
material = source.data.materials[0]
inner = bpy.data.materials.new('Puppet_Ink_Inner')
inner.diffuse_color = (.038, .043, .048, 1)
inner.use_nodes = True
inner.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = inner.diffuse_color
inner.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .95

# Add exact joint/waist intersections without changing the source surface.
mesh = source.data.copy()
bm = bmesh.new(); bm.from_mesh(mesh)
for axis, value in [(0, -.70), (0, -.47), (0, -.245), (0, .245), (0, .47), (0, .70), (2, .16), (2, 1.055), (2, 1.45)]:
    co = Vector((0, 0, 0)); co[axis] = value
    normal = Vector((0, 0, 0)); normal[axis] = 1
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces), dist=1e-6, plane_co=co, plane_no=normal, clear_inner=False, clear_outer=False)
# Six vertical panel seams. Their roots all stay at the existing waistband.
for degrees in (30, 90, 150):
    angle = math.radians(degrees)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces), dist=1e-6, plane_co=(0,0,0), plane_no=(math.cos(angle),math.sin(angle),0), clear_inner=False, clear_outer=False)
bm.to_mesh(mesh); bm.free(); mesh.update()
image = next(n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image)
pixels = np.empty(len(image.pixels), dtype=np.float32); image.pixels.foreach_get(pixels)
pixels = pixels.reshape(image.size[1], image.size[0], 4)
uv = mesh.uv_layers.active.data

def brightness(poly):
    u = sum(uv[l].uv.x for l in poly.loop_indices)/len(poly.loop_indices)
    v = sum(uv[l].uv.y for l in poly.loop_indices)/len(poly.loop_indices)
    c = pixels[min(image.size[1]-1,max(0,int(v*image.size[1]))),min(image.size[0]-1,max(0,int(u*image.size[0]))),:3]
    return float(c.mean())

def region(poly):
    x,y,z = poly.center; ax=abs(x); side='L' if x>=0 else 'R'
    if z>1.45 or (z>1.245 and ax<.26 and y>.065 and brightness(poly)>.39) or (z>1.315 and ax<.075 and y<-.09 and brightness(poly)>.45):
        return 'head'
    if ax>.245 and z>.89:
        return ('hand_' if ax>.70 else 'forearm_' if ax>.47 else 'upperarm_')+side
    if z<.16:
        return 'foot_'+side
    if z<1.055:
        angle=math.degrees(math.atan2(x,-y))%360
        return 'robe_'+str(int((angle+30)//60)%6)
    return 'chest'

groups={}
for p in mesh.polygons: groups.setdefault(region(p),[]).append(p.index)
parts=[]
for name, indices in groups.items():
    used=sorted({v for i in indices for v in mesh.polygons[i].vertices})
    mapping={v:i for i,v in enumerate(used)}
    data=bpy.data.meshes.new('Surface_'+name)
    data.from_pydata([mesh.vertices[i].co.copy() for i in used],[],[[mapping[v] for v in mesh.polygons[i].vertices] for i in indices])
    data.materials.append(material);data.materials.append(inner)
    layer=data.uv_layers.new(name='UVMap')
    for dest, oldidx in zip(data.polygons,indices):
        old=mesh.polygons[oldidx];dest.use_smooth=old.use_smooth
        for dl,sl in zip(dest.loop_indices,old.loop_indices):layer.data[dl].uv=uv[sl].uv
    obj=bpy.data.objects.new('Puppet_'+name,data);scene.collection.objects.link(obj)
    obj['part_role']=name;obj['source']='9cf92743 corrected Tripo surface';parts.append(obj)
    if name.startswith('robe_') or 'arm_' in name or name=='chest':
        solid=obj.modifiers.new('Approved panel backs and cut rims','SOLIDIFY');solid.thickness=.004;solid.offset=-1;solid.use_even_offset=False;solid.material_offset=1;solid.material_offset_rim=1

# Preserve originals in the separate source Blend, and remove it from this build.
bpy.data.objects.remove(source,do_unlink=True)

rigdata=bpy.data.armatures.new('FuYuan_Puppet_Rig')
rig=bpy.data.objects.new('FuYuan_Puppet_Rig',rigdata);scene.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
specs={'root':((0,0,0),(0,0,.1),None), 'hips':((0,0,.97),(0,0,1.055),'root'), 'chest':((0,0,1.055),(0,0,1.41),'hips'), 'head':((0,0,1.46),(0,0,1.70),'chest')}
for side,sign in [('L',1),('R',-1)]:
    specs['upperarm_'+side]=((sign*.245,0,1.34),(sign*.47,0,1.34),'chest')
    specs['forearm_'+side]=((sign*.47,0,1.34),(sign*.70,0,1.34),'upperarm_'+side)
    specs['hand_'+side]=((sign*.70,0,1.34),(sign*.86,0,1.34),'forearm_'+side)
    specs['thigh_'+side]=((sign*.14,0,.97),(sign*.16,0,.55),'hips')
    specs['shin_'+side]=((sign*.16,0,.55),(sign*.19,0,.13),'thigh_'+side)
    specs['foot_'+side]=((sign*.19,0,.13),(sign*.19,-.12,.055),'shin_'+side)
for n in range(6):
    a=math.radians(n*60)
    specs['robe_'+str(n)]=((.08*math.sin(a),-.08*math.cos(a),1.055),(.23*math.sin(a),-.23*math.cos(a),.24),'hips')
for name,(head,tail,parent) in specs.items():
    bone=rigdata.edit_bones.new(name);bone.head=head;bone.tail=tail
    if parent:bone.parent=rigdata.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front=True

def bind(obj,bone):
    group=obj.vertex_groups.new(name=bone);group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    modifier=obj.modifiers.new('Rigid joint - one bone per part','ARMATURE');modifier.object=rig
    obj.parent=rig

for obj in parts: bind(obj,obj['part_role'])

# Specifically authorized hidden articulated limbs. They never replace the painted exterior.
for side in ['L','R']:
    for role,r1,r2 in [('upperarm',.045,.047),('forearm',.042,.036),('thigh',.072,.065),('shin',.063,.048)]:
        bone=role+'_'+side;head,tail,_=specs[bone];head=Vector(head);tail=Vector(tail)
        delta=tail-head
        bpy.ops.mesh.primitive_cone_add(vertices=8,radius1=r2,radius2=r1,depth=delta.length+.025,location=(head+tail)/2)
        obj=bpy.context.object;obj.name='Internal_'+bone;obj.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
        bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
        obj.data.materials.append(inner);obj['part_role']=bone;obj['source']='approved hidden joint support'
        bind(obj,bone);parts.append(obj)

scene.render.fps=30
scene.frame_start=1;scene.frame_end=61
bpy.context.view_layer.update()
rig['design']='Rigid separate shell pieces; no cloth or spring physics; whole hands.'
rig['source_task']='9cf92743-4b3f-40bb-a4b0-987de9f87376'
(ROOT/'qa-corrected/parts.json').write_text(json.dumps({'source':rig['source_task'],'parts':[{'object':o.name,'bone':o['part_role'],'vertices':len(o.data.vertices),'source':o['source']} for o in parts]},ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'working/fuyuan-rigid-puppet.blend'))
result={'parts':len(parts),'bones':len(rigdata.bones),'part_names':[o.name for o in parts]}
