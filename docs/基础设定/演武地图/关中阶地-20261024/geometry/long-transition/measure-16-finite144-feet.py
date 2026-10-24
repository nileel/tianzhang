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

data=json.loads((Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'15-finite144-route-full-triangles.json').read_text())
foot_groups={name:np.flatnonzero((v[:,1]<=.01)&(v[:,0]*sx>0)&(v[:,2]*sz>0)) for name,sx,sz in [('front_left',-1,1),('front_right',1,1),('rear_left',-1,-1),('rear_right',1,-1)]}
out=[];worst_foot=None;max_footgap=0;void_feet=[];outside_cases=[];max_turn_angle_step=0;max_rotation_chord=0;max_ztravel=0
R=float(np.linalg.norm(v,axis=1).max())
previous={}
for row in data['rows']:
 mat=rotation(math.radians(row['yaw']),math.radians(row['pitch']),math.radians(row['roll']))
 w=v@mat.T+np.array([row['center'][0],row['root'],row['center'][1]]);cr=cube_round(w[:,[0,2]]);gap=np.full(len(v),np.inf);valid=np.zeros(len(v),dtype=bool)
 for qr in np.unique(cr,axis=0):
  ids=np.flatnonzero(np.all(cr==qr,axis=1));cell=cells.get(tuple(qr))
  if cell is None:continue
  gap[ids]=w[ids,1]-surface(cell['physicalSurfaceId'],w[ids,2]);valid[ids]=True
 foot={}
 for name,ids in foot_groups.items():
  best=int(ids[np.argmin(gap[ids])]);g=float(gap[best]);foot[name]={'minimum_actual_surface_gap_m':None if not math.isfinite(g) else g,'support_vertex':best,'supported_sole_vertices':int(valid[ids].sum()),'sole_vertices':len(ids)}
  if not math.isfinite(g):void_feet.append({'label':row['label'],'yaw':row['yaw'],'foot':name})
  elif g>max_footgap:max_footgap=g;worst_foot={'label':row['label'],'yaw':row['yaw'],'foot':name,'gap':g,'support_vertex':best}
 if row['vertices_outside_map']:
  ids=np.flatnonzero(~valid);outside_cases.append({'label':row['label'],'yaw':row['yaw'],'vertices':len(ids),'local_bounds_min':v[ids].min(axis=0).tolist(),'local_bounds_max':v[ids].max(axis=0).tolist(),'sole_vertices_outside':int((v[ids,1]<=.01).sum()),'tail_vertices_outside':int((v[ids,2]<-.3).sum()),'feet':foot})
 # Interpolating upright yaw/pitch/roll and root between these measured samples: bound rigid-point arc/chord deviation.
 key=row['label'].split(':move:')[0]+':move' if ':move:' in row['label'] else row['label']
 if key in previous:
  old,ow=previous[key];angle_sum=math.radians(abs(row['yaw']-old['yaw'])+abs(row['pitch']-old['pitch'])+abs(row['roll']-old['roll']))
  if angle_sum<math.pi/2:
   chord=(R+.5)*angle_sum*angle_sum/8
   max_rotation_chord=max(max_rotation_chord,chord);max_ztravel=max(max_ztravel,float(abs(w[:,2]-ow[:,2]).max()))
 previous[key]=(row,w)
summary={'poses':len(data['rows']),'individual_foot_wholly_outside_support_count':len(void_feet),'max_individual_foot_gap_to_actual_finite_surface_m':max_footgap,'worst_foot':worst_foot,'outside_map_pose_count':len(outside_cases),'outside_sole_pose_count':sum(x['sole_vertices_outside']>0 for x in outside_cases),'maximum_sole_vertices_outside':max(x['sole_vertices_outside'] for x in outside_cases),'max_rigid_rotation_chord_error_bound_m':max_rotation_chord,'max_vertex_Z_travel_between_adjacent_samples_m':max_ztravel,'mesh_max_3d_radius_m':R,'interpolation_scope':'Chord bound applies to piecewise linear yaw/pitch/roll plus root interpolation for actual rigid vertices. Surface curvature contributes K*deltaZ^2/8; discontinuous foreign side interfaces still require finite sweep clearance.'}
result={'summary':summary,'void_feet':void_feet,'outside_cases':outside_cases}
(Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'16-finite144-feet-and-motion-bounds.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'summary':summary,'outside_examples':outside_cases[:2]}
