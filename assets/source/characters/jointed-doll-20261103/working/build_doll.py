import bpy, bmesh, math, json
from mathutils import Vector, Matrix
ROOT='D:/天章游戏开发/assets/source/characters/jointed-doll-20261103/'
body=bpy.data.objects['Body_Source']
body.name='Body'
bpy.ops.object.select_all(action='DESELECT')
rig_data=bpy.data.armatures.new('JointedDollSkeleton')
rig=bpy.data.objects.new('JointedDollRig',rig_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
def bone(name,head,tail,parent=None):
 b=rig_data.edit_bones.new(name);b.head=head;b.tail=tail
 if parent:b.parent=rig_data.edit_bones[parent]
 b.use_connect=False
 return b
bone('root',(0,0,0),(0,0,.15))
bone('hips',(0,-.04,.95),(0,-.04,1.08),'root')
bone('spine',(0,-.04,1.08),(0,-.04,1.22),'hips')
bone('chest',(0,-.04,1.22),(0,-.04,1.43),'spine')
bone('head',(0,0,1.43),(0,0,1.76),'chest')
for side,s in [('L',1),('R',-1)]:
 bone('upper_arm.'+side,(s*.15,-.03,1.39),(s*.412,-.03,1.365),'chest')
 bone('forearm.'+side,(s*.412,-.03,1.365),(s*.69,-.03,1.35),'upper_arm.'+side)
 bone('hand.'+side,(s*.69,-.03,1.35),(s*.86,-.03,1.34),'forearm.'+side)
 bone('thigh.'+side,(s*.105,-.035,.94),(s*.12,-.035,.565),'hips')
 bone('shin.'+side,(s*.12,-.035,.565),(s*.145,-.035,.145),'thigh.'+side)
 bone('foot.'+side,(s*.145,-.035,.145),(s*.145,-.175,.08),'shin.'+side)
 bone('sleeve.'+side,(s*.465,.03,1.365),(s*.465,.03,.95),'forearm.'+side)
 bone('front_panel.'+side,(s*.115,-.15,1.055),(s*.115,-.15,.3),'hips')
 bone('rear_panel.'+side,(s*.085,.025,1.055),(s*.085,.025,.3),'hips')
bpy.ops.object.mode_set(mode='OBJECT')
def clamp(x):return max(0.,min(1.,x))
def mix(a,b,t):
 t=clamp(t);return {a:1-t,b:t}
def weight_for(v):
 x,y,z=v;ax=abs(x);side='L' if x>=0 else 'R'
 if y>.07 and z>1.18 and ax<.20:return {'head':1.}
 if ax>.14 and 1.285<z<1.465:
  if ax<.24:return mix('chest','upper_arm.'+side,(ax-.14)/.10)
  if ax<.375:return {'upper_arm.'+side:1.}
  if ax<.45:return mix('upper_arm.'+side,'forearm.'+side,(ax-.375)/.075)
  if ax<.665:return {'forearm.'+side:1.}
  if ax<.715:return mix('forearm.'+side,'hand.'+side,(ax-.665)/.05)
  return {'hand.'+side:1.}
 if z>1.48 or (y>.07 and z>1.18) or (y<-.12 and z>1.385 and ax<.075):return {'head':1.}
 if z>1.405:return mix('chest','head',(z-1.405)/.075)
 if z>1.20:return {'chest':1.}
 if z>1.12:return mix('spine','chest',(z-1.12)/.08)
 if z>1.06:return {'spine':1.}
 if z>.99:return mix('hips','spine',(z-.99)/.07)
 if z>.91:return {'hips':1.}
 if z>.82:return mix('thigh.'+side,'hips',(z-.82)/.09)
 if z>.61:return {'thigh.'+side:1.}
 if z>.525:return mix('shin.'+side,'thigh.'+side,(z-.525)/.085)
 if z>.18:return {'shin.'+side:1.}
 if z>.115:return mix('foot.'+side,'shin.'+side,(z-.115)/.065)
 return {'foot.'+side:1.}
def bind(obj,single=None):
 groups={b.name:obj.vertex_groups.new(name=b.name) for b in rig_data.bones}
 for v in obj.data.vertices:
  weights={single:1.} if single else weight_for(v.co)
  for n,w in weights.items():
   if w>1e-6:groups[n].add([v.index],w,'REPLACE')
 mod=obj.modifiers.new('JointedDollSkin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False
 obj.parent=rig
bind(body)
meshes=[body]
for unit,prefix,height,width,pitch in [('sleeve','sleeve',.48,.28,0),('front-panel','front_panel',.90,.32,-10),('rear-panel','rear_panel',.90,.42,10)]:
 source=bpy.data.objects[unit+'_Source']
 pts=[v.co for v in source.data.vertices]
 xmin=min(p.x for p in pts);xmax=max(p.x for p in pts);zmin=min(p.z for p in pts);zmax=max(p.z for p in pts)
 sx=width/(xmax-xmin);sz=height/(zmax-zmin)
 for side,s in [('L',1),('R',-1)]:
  mesh=source.data.copy();obj=bpy.data.objects.new(prefix+'.'+side,mesh);bpy.context.scene.collection.objects.link(obj)
  yaw=0 if unit=='sleeve' else s*(32 if unit=='front-panel' else 145)
  rot=Matrix.Rotation(math.radians(pitch),4,'X')@Matrix.Rotation(math.radians(yaw),4,'Z')
  anchor=rig_data.bones[prefix+'.'+side].head_local
  for v in mesh.vertices:
   q=Vector(((v.co.x-(xmin+xmax)/2)*sx*s,v.co.y*sz,(v.co.z-zmax)*sz+.027))
   v.co=rot@q+anchor
  if s<0:
   bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
  mesh.update();bind(obj,prefix+'.'+side);meshes.append(obj)
 bpy.data.objects.remove(source,do_unlink=True)
for obj in meshes:
 obj.hide_render=False;obj.hide_set(False)
 for mat in obj.data.materials:
  for n in mat.node_tree.nodes:
   if n.type=='BSDF_PRINCIPLED':n.inputs['Roughness'].default_value=.85;n.inputs['Metallic'].default_value=0
bpy.context.view_layer.update()
scene=bpy.context.scene;scene.frame_set(1)
for side,s in [('L',1),('R',-1)]:
 pb=rig.pose.bones['upper_arm.'+side];rest=pb.bone.matrix_local.to_quaternion();pb.rotation_mode='QUATERNION';pb.rotation_quaternion=rest.inverted()@Matrix.Rotation(math.radians(s*70),4,'Y').to_quaternion()@rest
bpy.context.view_layer.update()
for side in ['L','R']:
 pb=rig.pose.bones['sleeve.'+side];pr=pb.parent;parent_delta=pr.matrix.to_quaternion()@pr.bone.matrix_local.to_quaternion().inverted();rest=pb.bone.matrix_local.to_quaternion();pb.rotation_mode='QUATERNION';pb.rotation_quaternion=rest.inverted()@parent_delta.inverted()@rest
bpy.context.view_layer.update()
scene.camera.location=(2.5,-4,1.6);scene.camera.rotation_euler=(Vector((0,0,.9))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=ROOT+'working/assembly-idle.png';bpy.ops.render.render(write_still=True)
for pb in rig.pose.bones:pb.rotation_mode='QUATERNION';pb.rotation_quaternion=(1,0,0,0);pb.location=(0,0,0)
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'working/jointed-doll-rig-base.blend')
result={'meshes':[{'name':o.name,'verts':len(o.data.vertices),'faces':len(o.data.polygons),'materials':[m.name for m in o.data.materials]} for o in meshes],'bones':len(rig_data.bones),'mode':'continuous body with localized joint weights; six rigid single-bone panels'}
with open(ROOT+'working/rig-summary.json','w',encoding='utf-8') as f:json.dump(result,f,ensure_ascii=False,indent=2)
