import bpy,numpy as np,math,json
from pathlib import Path
scene=bpy.data.scenes['D1_Measure_20261024'];o=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'))
v=np.array([list(x.co) for x in o.data.vertices])*min(1.8/.99951172,.9/.45556642)
edges=np.array([list(e.vertices) for e in o.data.edges])
L=1.125;H=.75;N=8;start=(1-L)/2;step=L/N;rise=H/N
def terrain(x,kind):
 t=np.clip((x-start)/L,0,1)
 return H*t if kind=='ramp' else rise*np.floor(N*t+1e-8)
def evaluate(center,sign,angle,kind):
 C=math.cos(angle);S=math.sin(angle)
 xx=center+sign*(C*v[:,2]-S*v[:,1]);yy=S*v[:,2]+C*v[:,1]
 points=[v];world_x=[xx];world_y=[yy]
 x0=xx[edges[:,0]];x1=xx[edges[:,1]]
 boundaries=[start,start+L] if kind=='ramp' else [start+k*step for k in range(N+1)]
 for b in boundaries:
  take=(np.minimum(x0,x1)<b)&(np.maximum(x0,x1)>b)&(abs(x1-x0)>1e-12)
  ed=edges[take];tt=(b-x0[take])/(x1[take]-x0[take])
  local=v[ed[:,0]]+tt[:,None]*(v[ed[:,1]]-v[ed[:,0]])
  points.append(local);world_x.append(np.full(len(ed),b));world_y.append(S*local[:,2]+C*local[:,1])
 pp=np.concatenate(points);xx=np.concatenate(world_x);yy=np.concatenate(world_y)
 deficits=terrain(xx,kind)-yy
 supports=[]
 for name,sx,sz in [('front_left',-1,1),('front_right',1,1),('rear_left',-1,-1),('rear_right',1,-1)]:
  ids=np.flatnonzero((sx*pp[:,0]>0)&(sz*pp[:,2]>0)&(pp[:,1]<=.01))
  i=int(ids[np.argmax(deficits[ids])]);supports.append({'name':name,'needed_root_y':float(deficits[i]),'local_contact_xyz':pp[i].tolist(),'world_axial':float(xx[i]),'terrain_y':float(terrain(xx[i],kind)),'source':'vertex' if i<len(v) else 'edge/riser intersection'})
 allreq=float(deficits.max());front=max(r['needed_root_y'] for r in supports[:2]);rear=max(r['needed_root_y'] for r in supports[2:])
 return {'pitch_deg':math.degrees(angle),'root_y':allreq,'support_root_difference_front_minus_rear':front-rear,'max_foot_gap_after_fit':max(allreq-r['needed_root_y'] for r in supports),'feet':supports,'sampled_points_including_exact_edges':len(pp),'lowest_is_body_not_foot':allreq>max(front,rear)+.002}
rows=[]
for kind in ['ramp','stairs']:
 for sign in [1,-1]:
  a=sign*math.atan2(H,L)
  raw=evaluate(.5,sign,a,kind)
  # Exactly one contact-envelope angle solve on fixed profile; bracket is whole physically relevant tilt range.
  lo,hi=math.radians(-45),math.radians(45);fl=evaluate(.5,sign,lo,kind)['support_root_difference_front_minus_rear'];fh=evaluate(.5,sign,hi,kind)['support_root_difference_front_minus_rear']
  solved=None
  if fl*fh<=0:
   for j in range(24):
    m=(lo+hi)/2;fm=evaluate(.5,sign,m,kind)['support_root_difference_front_minus_rear']
    if fl*fm<=0:hi=m;fh=fm
    else:lo=m;fl=fm
   solved=evaluate(.5,sign,(lo+hi)/2,kind)
  rows.append({'kind':kind,'facing':sign,'center':.5,'fixed_profile_pitch':raw,'foot_envelope_pitch_solved':solved})
result={'scope':'D1 single L1125/8step trial including all mesh vertices plus every mesh edge intersection with support-profile break lines. This gives exact piecewise-linear triangle envelope in axial terrain profile, not an asset or runtime acceptance.','rows':rows}
Path(r'D:\Temp\TianZhang-Blender\d1-approved-20261024\14-L1125-triangle-contact-envelope.json').write_text(json.dumps(result,indent=2),encoding='utf8')
