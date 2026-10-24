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
rows=[]
for yaw_deg in [90,150,210,270,330,30]:
 yaw=math.radians(yaw_deg)
 for center in np.linspace(-1,L+1,129):
  g=derivative(center);p0=math.atan(g*math.cos(yaw));roll=-math.atan2(g*math.sin(yaw),math.sqrt(1+(g*math.cos(yaw))**2))
  lo=p0-math.radians(20);hi=p0+math.radians(20);fl=evaluate(center,yaw,lo,roll);fh=evaluate(center,yaw,hi,roll)
  if fl*fh>0:rows.append({'yaw':yaw_deg,'center':float(center),'failed_bracket':True});continue
  for j in range(24):
   mid=(lo+hi)/2;fm=evaluate(center,yaw,mid,roll)
   if fl*fm<=0:hi=mid
   else:lo=mid;fl=fm
  pitch=(lo+hi)/2;res=evaluate(center,yaw,pitch,roll,True)
  rows.append({'yaw':yaw_deg,'center':float(center),'pitch_deg':math.degrees(pitch),'roll_deg':math.degrees(roll),'tangent_pitch_deg':math.degrees(p0),**res})
good=[r for r in rows if not r.get('failed_bracket')]
worst=max(good,key=lambda r:r['max_front_rear_gap'])
summary={'L':L,'H':H,'row_intervals':5,'profile':'H/2*(1-cos(pi*z/L)), flat outside 0..L','rows':len(rows),'failed_brackets':len(rows)-len(good),'max_front_rear_foot_gap_m':float(worst['max_front_rear_gap']),'worst':worst,'max_triangle_interpolation_error_m':max(r['triangle_surface_interpolation_error_bound_m'] for r in good),'foot_gap_above_2mm_count':int(sum(r['max_front_rear_gap']>.002 for r in good)),'max_lateral_body_width':max(r['path_lateral_width'] for r in good)}
result={'summary':summary,'rows':rows,'scope':'D1 one curvature-derived C1 cosine terrain profile. All real vertices; front/rear actual sole support, roll follows world-Z tangent, pitch balances fore/hind contacts preserving horizontal yaw. Global triangle interpolation error rigorously bounded by K*max(projected triangle Z range)^2/8. Diagnostic longitudinal samples, not runtime or formal terrain asset.'}
p=Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'));(p/'02-cosine-five-row-six-yaw.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'summary':summary}
