import bpy, math, json
from pathlib import Path
from mathutils import Vector, Quaternion
ROOT=Path('D:/天章游戏开发/assets/source/characters/paper-puppet-skill-trial-20261103')
scene=bpy.context.scene
rig=bpy.data.objects['FuYuan_Puppet_Rig']
axes={'X':Vector((1,0,0)),'Y':Vector((0,1,0)),'Z':Vector((0,0,1))}

def turn(name, rotations):
    q=Quaternion()
    for axis,degrees in rotations:q=Quaternion(axes[axis],math.radians(degrees))@q
    rest=rig.data.bones[name].matrix_local.to_quaternion()
    pb=rig.pose.bones[name];pb.rotation_mode='QUATERNION';pb.rotation_quaternion=rest.inverted()@q@rest

def arm(side,down=42,yaw=12,elbow=-18,foreyaw=12):
    sign=1 if side=='L' else -1
    turn('upperarm_'+side,[('Y',sign*down),('Z',-sign*yaw)])
    turn('forearm_'+side,[('Y',sign*elbow),('Z',-sign*foreyaw)])

def legs(phase,walk):
    bounce=(-.008+.002*math.cos(2*phase)) if walk else -.006
    # hips is vertical, so local Y is world Z.
    rig.pose.bones['hips'].location.y=bounce
    for side,offset in [('L',0),('R',math.pi)]:
        t=phase+offset
        target_y=-.07*math.sin(t) if walk else 0
        target_z=.13+(.025*max(0,math.sin(t)) if walk else 0)
        h=.97+bounce-target_z
        l1=rig.data.bones['thigh_'+side].length;l2=rig.data.bones['shin_'+side].length
        distance=min(math.hypot(target_y,h),l1+l2-.0001)
        angle=math.atan2(target_y,h)
        a=angle-math.acos(max(-1,min(1,(l1*l1+distance*distance-l2*l2)/(2*l1*distance))))
        b=math.pi-math.acos(max(-1,min(1,(l1*l1+l2*l2-distance*distance)/(2*l1*l2))))
        turn('thigh_'+side,[('X',math.degrees(a))]);turn('shin_'+side,[('X',math.degrees(b))]);turn('foot_'+side,[('X',-math.degrees(a+b))])

def interpolate(t,keys):
    for (ta,va),(tb,vb) in zip(keys,keys[1:]):
        if ta<=t<=tb:
            u=(t-ta)/(tb-ta);u=u*u*(3-2*u)
            return va+(vb-va)*u
    return keys[-1][1]

def pose(kind,t):
    for pb in rig.pose.bones:pb.location=(0,0,0);pb.rotation_mode='QUATERNION';pb.rotation_quaternion=Quaternion();pb.scale=(1,1,1)
    phase=t*2*math.pi
    arm('L');arm('R');legs(phase,kind=='walk')
    for n in range(6):
        angle=math.radians(n*60)
        # Separate rigid panels fan out slightly, with restrained keyed gait motion.
        outward=6 if kind=='walk' else 4
        swing=2*math.sin(phase+(0 if n<3 else math.pi)) if kind=='walk' else 0
        turn('robe_'+str(n),[('X',-outward*math.cos(angle)+swing),('Y',-outward*math.sin(angle))])
    if kind=='walk':
        arm('L',42+7*math.sin(phase),12, -18,12+7*math.sin(phase))
        arm('R',42-7*math.sin(phase),12, -18,12-7*math.sin(phase))
        turn('chest',[('Z',2*math.sin(phase))])
    elif kind=='cast':
        amount=interpolate(t,[(0,0),(.28,1),(.65,1),(.87,.45),(1,0)])
        arm('L',42-30*amount,12+53*amount,-18-22*amount,12+12*amount)
        arm('R',42-30*amount,12+53*amount,-18-22*amount,12+12*amount)
    elif kind=='attack':
        thrust=interpolate(t,[(0,0),(.25,-.35),(.44,1),(.54,1),(.85,.15),(1,0)])
        arm('R',42-30*thrust,12+72*thrust,-18+16*thrust,12-7*thrust)
        arm('L',42-9*thrust,12+12*thrust,-18-22*thrust,12+20*thrust)
        turn('chest',[('Z',-9*thrust),('X',4*max(0,thrust))])

rig.animation_data_create()
for old in list(bpy.data.actions):
    if old.name in ('walk','cast','attack'):bpy.data.actions.remove(old)
durations={'walk':41,'cast':61,'attack':46}
for name,count in durations.items():
    action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data.action=action
    for frame in range(1,count+1):
        pose(name,(frame-1)/(count-1))
        for pb in rig.pose.bones:
            pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
            if pb.name=='hips':pb.keyframe_insert('location',frame=frame,group=pb.name)
    action['purpose']='Rigid paper puppet experiment; authored in-place; no physics'
rig.animation_data.action=bpy.data.actions['walk'];scene.frame_end=41;scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'working/fuyuan-rigid-puppet.blend'))

cam=scene.camera;cam.data.ortho_scale=2.12
for action,frame in [('walk',11),('cast',31),('attack',21)]:
    rig.animation_data.action=bpy.data.actions[action];scene.frame_set(frame)
    for label,position in [('front',(0,-4,.9)),('angle',(3,-5,2.4))]:
        cam.location=position;cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(ROOT/('qa-corrected/puppet-'+action+'-'+label+'.png'));bpy.ops.render.render(write_still=True)
rig.animation_data.action=bpy.data.actions['walk'];scene.frame_set(1)
result={'actions':durations,'physics':False,'file':bpy.data.filepath}
