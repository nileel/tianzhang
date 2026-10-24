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

import bmesh
bm=bmesh.new();bm.from_mesh(o.data);hull_result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
hverts=set()
for elem in hull_result['geom']:
 if isinstance(elem,bmesh.types.BMVert):hverts.add(elem)
 elif isinstance(elem,bmesh.types.BMFace):hverts.update(elem.verts)
hv=np.array([list(x.co) for x in hverts])*min(1.8/.99951172,.9/.45556642);bm.free()
def hull2(points):
 pts=sorted(set(map(tuple,points)))
 def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
 lower=[]
 for q in pts:
  while len(lower)>=2 and cross(lower[-2],lower[-1],q)<=0:lower.pop()
  lower.append(q)
 upper=[]
 for q in reversed(pts):
  while len(upper)>=2 and cross(upper[-2],upper[-1],q)<=0:upper.pop()
  upper.append(q)
 return np.array(lower[:-1]+upper[:-1])
hexverts=np.array([[math.cos(math.pi/6+k*math.pi/3),math.sin(math.pi/6+k*math.pi/3)] for k in range(6)])/math.sqrt(3)
def separation(poly,hexp):
 de=np.roll(poly,-1,axis=0)-poly;axes=np.r_[normals,np.column_stack((-de[:,1],de[:,0]))/np.linalg.norm(de,axis=1)[:,None]]
 a=poly@axes.T;b=hexp@axes.T
 return float(np.max(np.maximum(b.min(axis=0)-a.max(axis=0),a.min(axis=0)-b.max(axis=0))))
def dominated(cellpid,pid,zmin,zmax):
 if cellpid==pid:return True
 if cellpid in profiles and pid in profiles:
  a=profiles[cellpid];b=profiles[pid]
  if abs(a['worldZStart']-b['worldZStart'])<1e-8 and abs(a['lowerY']-b['lowerY'])<1e-8:return True
 return float(surface(cellpid,np.array(zmax)))<=float(surface(pid,np.array(zmin)))+1e-9
data=json.loads((Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'15-finite144-route-full-triangles.json').read_text())
last={};checks=[];global_bound=0;radius=float(np.linalg.norm(v,axis=1).max())
for row in data['rows']:
 mat=rotation(math.radians(row['yaw']),math.radians(row['pitch']),math.radians(row['roll']))
 w=hv@mat.T+np.array([row['center'][0],row['root'],row['center'][1]])
 key=row['label'].split(':move:')[0]+':move' if ':move:' in row['label'] else row['label']
 if key not in last:last[key]=(row,w);continue
 old,ow=last[key];last[key]=(row,w)
 angle_sum=math.radians(abs(row['yaw']-old['yaw'])+abs(row['pitch']-old['pitch'])+abs(row['roll']-old['roll']))
 arc=(radius+.5)*angle_sum**2/8
 poly=hull2(np.r_[ow[:,[0,2]],w[:,[0,2]]]);minz=float(poly[:,1].min()-arc);maxz=float(poly[:,1].max()+arc)
 ztravel=float(abs(w[:,2]-ow[:,2]).max());smooth_error=.000155863+(1+.2720699046351327)*arc+K*(ztravel+2*arc)**2/8
 global_bound=max(global_bound,smooth_error)
 worstsep=1e9;worstcell=None
 for qr,cell in cells.items():
  cc=np.array([qr[0]+.5*qr[1],1+h*qr[1]])
  if cc[1]+1/math.sqrt(3)<minz or cc[1]-1/math.sqrt(3)>maxz:continue
  zmin=max(minz,cc[1]-1/math.sqrt(3));zmax=min(maxz,cc[1]+1/math.sqrt(3));sp=cell['physicalSurfaceId']
  if dominated(sp,row['profile'],zmin,zmax):continue
  sep=separation(poly,cc+hexverts)-arc
  if sep<worstsep:worstsep=sep;worstcell={'qr':list(qr),'surface':sp}
 checks.append({'label':key,'yaw0':old['yaw'],'yaw1':row['yaw'],'arc_chord_error_bound':arc,'same_profile_continuous_penetration_bound':smooth_error,'higher_foreign_domain_horizontal_clearance_lower_bound':None if worstcell is None else worstsep,'nearest_higher_foreign_cell':worstcell})
valid=[x for x in checks if x['higher_foreign_domain_horizontal_clearance_lower_bound'] is not None]
worst=min(valid,key=lambda x:x['higher_foreign_domain_horizontal_clearance_lower_bound'])
summary={'intervals':len(checks),'original_mesh_convex_hull_vertices':len(hv),'continuous_C1_surface_error_bound_m':global_bound,'engineering_tolerance_m':.002,'foreign_higher_surface_intervals_with_nonpositive_separation':sum(x['higher_foreign_domain_horizontal_clearance_lower_bound']<=0 for x in valid),'minimum_higher_foreign_domain_horizontal_clearance_bound_m':worst['higher_foreign_domain_horizontal_clearance_lower_bound'],'worst':worst,'method':'Convex hull of both endpoint rigid mesh projections contains every linear swept triangle. True rigid Euler interpolation deviates by <=(meshRadius+.5)*(sum absolute angle increments)^2/8. Expanded hull is separated from every foreign higher support hex, or that actual support is pointwise below the intended global profile over their shared Z range. Curvature bound includes full triangle interiors. No map geometry modified.'}
result={'summary':summary,'intervals':checks}
(Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'18-continuous-route-side-boundary-bound.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'summary':summary,'nonseparated':sorted(valid,key=lambda x:x['higher_foreign_domain_horizontal_clearance_lower_bound'])[:12]}
