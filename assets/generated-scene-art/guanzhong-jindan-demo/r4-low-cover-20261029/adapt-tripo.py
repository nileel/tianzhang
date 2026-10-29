import bpy, bmesh, json, hashlib, os
from mathutils import Matrix, Vector
root='D:/天章游戏开发'
raw=root+'/assets/source/environments/guanzhong-jindan-demo/r4-low-cover/raw/r4-low-cover-tripo-source.glb'
working=root+'/assets/source/environments/guanzhong-jindan-demo/r4-low-cover/working'
dest=root+'/src/Assets/Art/Environments/Guanzhong/Props'
assert not any(bpy.data.objects.get(n) for n in ['LowWallIntact','LowWallDamaged']), 'Run in a fresh Blender session to avoid datablock name collisions.'
scene=bpy.data.scenes.new('R4_LowCover_Export')
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
bpy.ops.import_scene.gltf(filepath=raw)
src=next(o for o in scene.objects if o.type=='MESH')
src.name='R4_Source_Unmodified'
mat=src.data.materials[0].copy();mat.name='LowWallTripoStone'
# The original Tripo geometry stays in its own collection; only a duplicate is fitted.
intact=src.copy();intact.data=src.data.copy();scene.collection.objects.link(intact);intact.name='LowWallIntact'
intact.data.materials[0]=mat
coords=[v.co for v in intact.data.vertices]
dims=[max(v[i] for v in coords)-min(v[i] for v in coords) for i in range(3)]
intact.data.transform(Matrix.Diagonal((.61/dims[0],.16/dims[1],.375/dims[2],1)))
intact.data.update()
damaged=intact.copy();damaged.data=intact.data.copy();scene.collection.objects.link(damaged);damaged.name='LowWallDamaged'
# Trim the upper masonry from the Tripo duplicate, then close the exposed break.
bm=bmesh.new();bm.from_mesh(damaged.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.0000001)
cut=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
 dist=0.0000001,plane_co=Vector((0,0,.205)),plane_no=Vector((.12,0,1)),clear_outer=True,clear_inner=False)
boundary=[e for e in cut['geom_cut'] if isinstance(e,bmesh.types.BMEdge) and e.is_boundary]
caps=bmesh.ops.holes_fill(bm,edges=boundary,sides=0)['faces']
uv=bm.loops.layers.uv.verify()
for face in caps:
 for loop in face.loops: loop[uv].uv=(.5+loop.vert.co.x/.61*.18,.5+loop.vert.co.y/.16*.08)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(damaged.data);bm.free();damaged.data.update()
image_node=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE')
tex=image_node.image
assert tuple(tex.size)==(4096,4096) and tex.packed_file
with open(dest+'/LowWall_basecolor.jpg','wb') as f:f.write(tex.packed_file.data)
proof={'source':raw,'sourceSha256':hashlib.sha256(open(raw,'rb').read()).hexdigest(),
 'textureSize':[4096,4096],'sourceBlenderVertices':len(src.data.vertices),
 'adaptation':'Tripo duplicate fitted to existing .61 x .16 x .375m envelope; damaged duplicate upper section trimmed and capped; shared rubble reused.',
 'models':[]}
for ob in [intact,damaged]:
 for other in scene.objects:other.select_set(False)
 ob.hide_set(False);ob.select_set(True);bpy.context.view_layer.objects.active=ob
 bpy.context.view_layer.update()
 path=dest+'/'+ob.name+'.fbx'
 bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'MESH'},
  axis_forward='-Z',axis_up='Y',bake_space_transform=True,apply_unit_scale=True,
  use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
 points=[v.co for v in ob.data.vertices];ob.data.calc_loop_triangles()
 proof['models'].append({'name':ob.name,'vertices':len(points),'triangles':len(ob.data.loop_triangles),
  'bounds':[[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]],
  'sha256':hashlib.sha256(open(path,'rb').read()).hexdigest()})
src.hide_render=True;src.hide_set(True);damaged.hide_set(True)
bpy.data.libraries.write(working+'/LowWall-Tripo.blend',{scene},fake_user=True,compress=True)
with open(working+'/adaptation-proof.json','w',encoding='utf-8') as f:json.dump(proof,f,ensure_ascii=False,indent=2)
result=proof
