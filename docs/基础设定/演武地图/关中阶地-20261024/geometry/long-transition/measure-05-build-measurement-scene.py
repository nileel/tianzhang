import bpy,numpy as np,math,json,hashlib
from pathlib import Path
from mathutils import Matrix,Vector
p=Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))
assert not (p/'d1-long-cosine-measurement.blend').exists()
source=bpy.data.scenes['D1_Measure_20261024']
beast=next(o for o in source.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'));player=next(o for o in source.objects if o.type=='MESH' and o.name.startswith('D1_Player_'))
data=json.loads((p/'02-cosine-five-row-six-yaw.json').read_text());pdata=json.loads((p/'04-player-common-profile.json').read_text())
H=.75;L=5*math.sqrt(3)/2
def h(z):return H*.5*(1-math.cos(math.pi*max(0,min(1,z/L))))
def frame(yaw,pitch,roll):
 sy,cy=math.sin(yaw),math.cos(yaw);sp,cp=math.sin(pitch),math.cos(pitch);sr,cr=math.sin(roll),math.cos(roll)
 f=np.array([sy*cp,sp,cy*cp]);r0=np.array([cy,0,-sy]);u0=np.cross(f,r0);r=r0*cr+u0*sr;u=-r0*sr+u0*cr
 return np.column_stack((r,u,f))
scene=bpy.data.scenes.new('D1_Long_C1_Measurement_20261024');scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
bpy.context.window.scene=scene
zs=np.unique(np.r_[np.linspace(-1,0,51),np.linspace(0,L,218),np.linspace(L,L+1,51)])
verts=[(x,float(z),h(float(z)))for z in zs for x in [-2,2]];faces=[(2*i,2*i+1,2*i+3,2*i+2)for i in range(len(zs)-1)]
me=bpy.data.meshes.new('D1_Analytical_Support_Surface_ONLY');me.from_pydata(verts,[],faces);me.update();surf=bpy.data.objects.new('D1_MEASUREMENT_ONLY_C1_SUPPORT_H075_L4330',me);scene.collection.objects.link(surf);surf.color=(.21,.37,.49,1)
conv=np.array([[1,0,0],[0,0,1],[0,1,0]])
poses=[]
for yaw,wanted,x,label in [(30,.58,-.85,'Uphill - actual sole support'),(90,L/2,.5,'Across slope - roll'),(150,L-.58,.9,'Downhill - actual sole support')]:
 r=min((r for r in data['rows'] if r['yaw']==yaw),key=lambda r:abs(r['center']-wanted));rot=np.array(r['matrix']);scale=min(1.8/.99951172,.9/.45556642);mat=np.eye(4);mat[:3,:3]=conv@rot*scale;mat[:3,3]=[x,r['center'],r['root_y']]
 obj=bpy.data.objects.new('D1_MEASURE_Beast_'+str(yaw),beast.data);scene.collection.objects.link(obj);obj.matrix_world=Matrix(mat);obj.color=(.64,.49,.30,1)
 raw=np.array([list(v.co) for v in beast.data.vertices])*scale;world=raw@rot.T+np.array([x,r['root_y'],r['center']])
 for key in ['front_vertex','rear_vertex']:
  v=world[r[key]];bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=.022,location=(float(v[0]),float(v[2]),float(v[1])));mark=bpy.context.object;mark.name='D1_CONTACT_'+str(yaw)+'_'+key;mark.color=(1,.10,.025,1)
 poses.append({'label':label,'yaw':yaw,'profile_center_z':r['center'],'profile_root_y':r['root_y'],'lateral_x':x,'pitch':r['pitch_deg'],'roll':r['roll_deg'],'front_vertex':r['front_vertex'],'rear_vertex':r['rear_vertex']})
r=min((r for r in pdata['rows'] if r['yaw']==30),key=lambda r:abs(r['center']-(L-.8)));rot=frame(math.radians(30),math.radians(r['pitch_deg']),math.radians(r['roll_deg']));mat=np.eye(4);mat[:3,:3]=conv@rot*1.4;mat[:3,3]=[-1.2,r['center'],r['root_y']];obj=bpy.data.objects.new('D1_MEASURE_Player_30',player.data);scene.collection.objects.link(obj);obj.matrix_world=Matrix(mat);obj.color=(.82,.82,.68,1)
camdat=bpy.data.cameras.new('D1_measure_camera');cam=bpy.data.objects.new('D1_measure_camera',camdat);scene.collection.objects.link(cam);cam.location=(9,-8,8);target=Vector((0,L/2,.6));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();camdat.type='ORTHO';camdat.ortho_scale=8.5;scene.camera=cam
for ob in scene.objects:ob.select_set(False)
bpy.context.view_layer.objects.active=None
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  sp=area.spaces.active;sp.shading.type='SOLID';sp.shading.light='STUDIO';sp.shading.color_type='OBJECT';sp.shading.show_shadows=True;sp.shading.show_cavity=True;sp.overlay.show_overlays=False;sp.region_3d.view_perspective='ORTHO';sp.region_3d.view_location=target;sp.region_3d.view_rotation=cam.rotation_euler.to_quaternion();sp.region_3d.view_distance=8.8
scene['D1_scope']='Analytical measurement surface only; no thickness, no Tile join, not D3 terrain asset.'
scene['D1_profile']='Y=.75/2*(1-cos(pi*Z/4.330127018922193)), Z clipped to0..L; Unity XYZ -> Blender XZY.'
bpy.context.view_layer.update()
imports=json.loads((p/'measurement-import-state.json').read_text())
hashes=[{'key':k,'path':e['path'],'expected':e['sha256'],'actual':hashlib.sha256(Path(e['path']).read_bytes()).hexdigest()}for k,e in imports.items()]
assert all(r['expected']==r['actual']for r in hashes)
bpy.ops.wm.save_as_mainfile(filepath=str(p/'d1-long-cosine-measurement.blend'),check_existing=False)
result={'scene':scene.name,'file':bpy.data.filepath,'surface_z_chord_max_m':float(np.diff(zs).max()),'surface_curve_chord_error_bound_m':float((H*math.pi**2/(2*L*L))*max(np.diff(zs))**2/8),'poses':poses,'source_hashes':hashes}
(p/'05-actual-scene-metadata.json').write_text(json.dumps(result,indent=2),encoding='utf8')
