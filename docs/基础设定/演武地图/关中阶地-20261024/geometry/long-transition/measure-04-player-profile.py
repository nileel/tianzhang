import bpy,numpy as np,math,json,time
from pathlib import Path
scene=bpy.data.scenes['D1_Measure_20261024'];o=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'))
v=np.array([list(x.co) for x in o.data.vertices])*min(1.8/.99951172,.9/.45556642)
faces=np.array([list(f.vertices) for f in o.data.polygons])
H=.75;L=5*math.sqrt(3)/2;K=H*math.pi**2/(2*L*L)
def profile(z):
 t=np.clip(z/L,0,1)
 return H*.5*(1-np.cos(math.pi*t))
def derivative(z):
 return H*math.pi/(2*L)*math.sin(math.pi*z/L) if 0<z<L else 0.
front=np.flatnonzero((v[:,2]>0)&(v[:,1]<=.01));rear=np.flatnonzero((v[:,2]<0)&(v[:,1]<=.01))
def rotation(yaw,pitch,roll):
 sy,cy=math.sin(yaw),math.cos(yaw);sp,cp=math.sin(pitch),math.cos(pitch);sr,cr=math.sin(roll),math.cos(roll)
 f=np.array([sy*cp,sp,cy*cp]);r0=np.array([cy,0,-sy]);u0=np.cross(f,r0);r=r0*cr+u0*sr;u=-r0*sr+u0*cr
 return np.column_stack((r,u,f))
def evaluate(center,yaw,pitch,roll,allmesh=False):
 mat=rotation(yaw,pitch,roll);idx=np.arange(len(v)) if allmesh else np.r_[front,rear];w=v[idx]@mat.T
 req=profile(center+w[:,2])-w[:,1]
 if allmesh:
  fi=front[np.argmax(req[front])];ri=rear[np.argmax(req[rear])];ai=int(np.argmax(req));root=float(req[ai]);span=np.ptp(w[faces,2],axis=1);bound=float(K*np.max(span*span)/8)
  return {'root_y':root,'front_required_y':float(req[fi]),'rear_required_y':float(req[ri]),'max_front_rear_gap':max(root-req[fi],root-req[ri]),'front_vertex':int(fi),'rear_vertex':int(ri),'body_limiting_vertex':ai,'body_limiting_local':v[ai].tolist(),'triangle_surface_interpolation_error_bound_m':bound,'matrix':mat.tolist(),'path_lateral_width':float(np.ptp(w@np.array([math.cos(yaw),0,-math.sin(yaw)]))),'horizontal_radius':float(np.linalg.norm(w[:,[0,2]],axis=1).max())}
 return float(req[:len(front)].max()-req[len(front):].max())

po=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('D1_Player_'))
pv=np.array([list(x.co) for x in po.data.vertices])*1.4
pf=np.array([list(f.vertices) for f in po.data.polygons])
sole=np.flatnonzero(pv[:,1]<=pv[:,1].min()+.01)
left=sole[pv[sole,0]<0];right=sole[pv[sole,0]>=0]
rows=[]
for yaw_deg in [90,150,210,270,330,30]:
 yaw=math.radians(yaw_deg)
 for center in np.linspace(-.5,L+.5,129):
  g=derivative(center);pitch=math.atan(g*math.cos(yaw));roll=-math.atan2(g*math.sin(yaw),math.sqrt(1+(g*math.cos(yaw))**2));mat=rotation(yaw,pitch,roll);w=pv@mat.T
  req=profile(center+w[:,2])-w[:,1];root=float(req.max());i=int(np.argmax(req));span=np.ptp(w[pf,2],axis=1);bound=float(K*np.max(span*span)/8)
  rows.append({'yaw':yaw_deg,'center':float(center),'pitch_deg':math.degrees(pitch),'roll_deg':math.degrees(roll),'root_y':root,'support_vertex':i,'support_local_xyz':pv[i].tolist(),'support_is_low_sole':bool(i in sole),'left_sole_min_gap':float(root-req[left].max()),'right_sole_min_gap':float(root-req[right].max()),'triangle_error_bound':bound})
summary={'mesh':po.name,'vertex_count':len(pv),'triangle_count':len(pf),'low_sole_counts_left_right':[len(left),len(right)],'poses':len(rows),'non_sole_limiting_count':sum(not r['support_is_low_sole'] for r in rows),'max_individual_foot_gap':max(max(r['left_sole_min_gap'],r['right_sole_min_gap']) for r in rows),'max_triangle_error_bound':max(r['triangle_error_bound'] for r in rows),'min_local_y':float(pv[:,1].min()),'max_gradient':H*math.pi/(2*L)}
result={'summary':summary,'rows':rows,'scope':'Single-cell player tilted to common C1 worldZ surface tangent, vertical contact fit using actual mesh. Whole low1cm sole band supports; no bbox substitute. This is D1 numeric mesh validation only.'}
(Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'04-player-common-profile.json').write_text(json.dumps(result,indent=2),encoding='utf8');result={'summary':summary}
