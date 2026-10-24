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

import hashlib
p=Path(globals().get('MAP_DIR',r'D:\Temp\TianZhang-D1-Approved-20261024\long-map'))
d=json.loads((p/'map-candidate.json').read_text(encoding='utf8'))
cells={(c['q'],c['r']):c for c in d['cells']}
profiles={x['id']:x for x in d['physicalSurfaceProfiles']}
stairs={x['id']:x for x in d['physicalStairConnections']}
h=math.sqrt(3)/2;baseY=.340433121
normals=np.array([[math.cos(k*math.pi/3),math.sin(k*math.pi/3)] for k in range(6)])
dirs=[(1,0),(1,-1),(0,-1),(-1,0),(-1,1),(0,1)]
sids=sorted(set(c['physicalSurfaceId'] for c in cells.values()));sidmap={x:i for i,x in enumerate(sids)}
def coord(s):
 q,r=map(int,s.split(':'));return np.array([q+.5*r,1+h*r])
def surface(pid,z):
 if pid in profiles:
  b=profiles[pid];return b['lowerY']+profile(z-b['worldZStart'])
 if pid in stairs:
  b=stairs[pid];zz=z-b['worldZStart'];start=(math.sqrt(3)-1.125)/2;upper=math.sqrt(3)/2+.25
  return b['lowerY']+.09375*(np.floor(4*np.clip((zz-start)/.3125,0,1)+1e-8)+np.floor(4*np.clip((zz-upper)/.3125,0,1)+1e-8))
 return np.full_like(z,baseY+.75*int(pid[-1]))
def flat_value(pid,zmin,zmax):
 if pid.startswith('flat_'):return baseY+.75*int(pid[-1])
 if pid in profiles:
  b=profiles[pid]
  if zmax<=b['worldZStart']:return b['lowerY']
  if zmin>=b['worldZEnd']:return b['upperY']
 return None
def cube_round(xz):
 rr=(xz[:,1]-1)/h;qq=xz[:,0]-.5*rr;ss=-qq-rr
 qi=np.rint(qq);ri=np.rint(rr);si=np.rint(ss);qd=abs(qi-qq);rd=abs(ri-rr);sd=abs(si-ss)
 fixq=(qd>rd)&(qd>sd);fixr=(~fixq)&(rd>sd)
 qi[fixq]=-ri[fixq]-si[fixq];ri[fixr]=-qi[fixr]-si[fixr]
 return np.column_stack((qi,ri)).astype(int)
def clip(poly,cc):
 for n in normals:
  out=[]
  if not len(poly):break
  prev=poly[-1];dp=float(np.dot(prev[[0,2]]-cc,n)-.5)
  for cur in poly:
   dc=float(np.dot(cur[[0,2]]-cc,n)-.5)
   if (dp<=1e-10)!=(dc<=1e-10):out.append(prev+(cur-prev)*(dp/(dp-dc)))
   if dc<=1e-10:out.append(cur)
   prev=cur;dp=dc
  poly=out
 return np.array(poly)
def pose(anchor,yawdeg,pid,label):
 yaw=math.radians(yawdeg);center=anchor-.5*np.array([math.sin(yaw),math.cos(yaw)])
 if pid in profiles:b=profiles[pid];cz=center[1]-b['worldZStart'];lower=b['lowerY'];g=derivative(cz)
 else:cz=-2;lower=baseY+.75*int(pid[-1]);g=0
 p0=math.atan(g*math.cos(yaw));roll=-math.atan2(g*math.sin(yaw),math.sqrt(1+(g*math.cos(yaw))**2));lo=p0-math.radians(20);hi=p0+math.radians(20);fl=evaluate(cz,yaw,lo,roll)
 for j in range(24):
  mid=(lo+hi)/2;fm=evaluate(cz,yaw,mid,roll)
  if fl*fm<=0:hi=mid
  else:lo=mid;fl=fm
 pitch=(lo+hi)/2;mat=rotation(yaw,pitch,roll);w=v@mat.T
 root=float((profile(cz+w[:,2])-w[:,1]).max())+lower
 w+=np.array([center[0],root,center[1]]);cr=cube_round(w[:,[0,2]])
 owners=np.full(len(v),-1,dtype=int);actual_gap=np.full(len(v),np.inf);penetration=0.;worst=None;missing=0
 for qr in np.unique(cr,axis=0):
  ids=np.flatnonzero(np.all(cr==qr,axis=1));cell=cells.get(tuple(qr))
  if cell is None:missing+=len(ids);continue
  sp=cell['physicalSurfaceId'];owners[ids]=sidmap[sp];gap=w[ids,1]-surface(sp,w[ids,2]);actual_gap[ids]=gap;i=int(np.argmin(gap))
  if -gap[i]>penetration:penetration=float(-gap[i]);wi=int(ids[i]);worst={'vertex':wi,'world':w[wi].tolist(),'cell':qr.tolist(),'surface':sp}
 # Every triangle inside one support region is bounded by K*Zspan^2/8.
 span=np.ptp(w[faces,2],axis=1);smooth_bound=float(K*np.max(span*span)/8)
 triangle_bound_gap=float(actual_gap.min()-smooth_bound);clipped=0;worst_triangle=None
 mixed=np.flatnonzero((np.min(owners[faces],axis=1)!=np.max(owners[faces],axis=1)))
 for ti in mixed:
  tri=w[faces[ti]];zmin=float(tri[:,2].min());zmax=float(tri[:,2].max())
  pids=[sids[x] for x in set(owners[faces[ti]]) if x>=0]
  vals=[flat_value(sp,zmin,zmax) for sp in pids]
  if len(vals)==len(pids) and vals and all(x is not None for x in vals) and max(vals)-min(vals)<1e-10:continue
  qs=set(tuple(x) for x in cr[faces[ti]])
  qs.update((q+dq,r+dr) for q,r in list(qs) for dq,dr in dirs)
  for qr in qs:
   cell=cells.get(qr)
   if cell is None:continue
   cc=np.array([qr[0]+.5*qr[1],1+h*qr[1]]);poly=clip(list(tri),cc)
   if not len(poly):continue
   sp=cell['physicalSurfaceId'];gap=poly[:,1]-surface(sp,poly[:,2]);bound=K*np.ptp(poly[:,2])**2/8 if sp in profiles else 0.;mg=float(gap.min()-bound);clipped+=1
   if mg<triangle_bound_gap:triangle_bound_gap=mg;worst_triangle={'triangle':int(ti),'cell':list(qr),'surface':sp,'conservative_gap':mg}
 return {'label':label,'anchor':anchor.tolist(),'yaw':yawdeg,'center':center.tolist(),'profile':pid,'pitch':math.degrees(pitch),'roll':math.degrees(roll),'root':root,'vertex_penetration':penetration,'worst':worst,'triangle_conservative_min_gap':triangle_bound_gap,'worst_triangle':worst_triangle,'mixed_triangle_clips':clipped,'smooth_triangle_error_bound':smooth_bound,'front_group_gap':float(actual_gap[front].min()),'rear_group_gap':float(actual_gap[rear].min()),'vertices_outside_map':missing}
rows=[];yaws=[90,150,210,270,330,30]
for rn in ['pair-main-route','pair-alternate-route']:
 route=json.loads((p/(rn+'.json')).read_text(encoding='utf8'))
 for mi,m in enumerate(route['movements']):
  fp=m['fromPose'];fy=fp['facing'];anchor=coord(fp['front'])
  for ti,t in enumerate(m['intermediateTurns']):
   tp=t['pose'];ny=tp['facing'];delta=((yaws[ny]-yaws[fy]+180)%360)-180
   pid=tp['frontPhysicalSurface'] if tp['frontPhysicalSurface'] in profiles else tp['rearPhysicalSurface'] if tp['rearPhysicalSurface'] in profiles else tp['frontPhysicalSurface']
   for angle in np.linspace(yaws[fy],yaws[fy]+delta,31):rows.append(pose(anchor,float(angle),pid,f'{rn}:{mi}:turn{ti}'))
   fy=ny
  to=m['toPose'];dest=coord(to['front']);pid=to['frontPhysicalSurface'] if to['frontPhysicalSurface'] in profiles else to['rearPhysicalSurface'] if to['rearPhysicalSurface'] in profiles else fp['frontPhysicalSurface'] if fp['frontPhysicalSurface'] in profiles else to['frontPhysicalSurface']
  for t in np.linspace(0,1,21):rows.append(pose(anchor*(1-t)+dest*t,yaws[to['facing']],pid,f'{rn}:{mi}:move:{t:.2f}'))
summary={'mapVersion':d['mapVersion'],'map_sha256':hashlib.sha256((p/'map-candidate.json').read_bytes()).hexdigest(),'poses':len(rows),'positive_vertex_penetration_above_1mm':sum(r['vertex_penetration']>.001 for r in rows),'max_vertex_penetration':max(r['vertex_penetration'] for r in rows),'minimum_triangle_conservative_gap':min(r['triangle_conservative_min_gap'] for r in rows),'triangles_negative_beyond_1mm_count':sum(r['triangle_conservative_min_gap']<-.001 for r in rows),'max_fore_hind_group_gap':max(max(r['front_group_gap'],r['rear_group_gap']) for r in rows),'max_vertices_outside_map':max(r['vertices_outside_map'] for r in rows),'mixed_triangle_clips':sum(r['mixed_triangle_clips'] for r in rows),'turn_yaw_step_deg':2,'translation_step_m':.05,'flat_turn_between_sample_point_displacement_upper_bound_m':2*1.400001*math.sin(math.radians(1)/2),'straight_between_sample_translation_upper_bound_m':.025}
result={'summary':summary,'rows':rows,'scope':'Finite144 map one joint verification. Full actual vertices, all mixed-support triangles clipped at actual ideal hex boundaries; smooth triangle interiors bounded by global curvature. Intermediate turn and movement samples are explicit, with indicated flat-turn and translation sampling displacement bounds; these bounds alone are not a collision clearance proof. No original assets modified.'}
out=Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'));(out/'15-finite144-route-full-triangles.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'summary':summary,'worst_vertex':max(rows,key=lambda r:r['vertex_penetration']),'worst_triangle':min(rows,key=lambda r:r['triangle_conservative_min_gap']),'worst_support':max(rows,key=lambda r:max(r['front_group_gap'],r['rear_group_gap']))}
