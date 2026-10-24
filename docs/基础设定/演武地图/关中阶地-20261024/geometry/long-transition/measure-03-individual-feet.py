import bpy,numpy as np,math,json
from pathlib import Path
p=Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))
j=json.loads((p/'02-cosine-five-row-six-yaw.json').read_text())
s=bpy.data.scenes['D1_Measure_20261024'];o=next(o for o in s.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'));v=np.array([list(x.co) for x in o.data.vertices])*min(1.8/.99951172,.9/.45556642)
H=.75;L=5*math.sqrt(3)/2;K=H*math.pi**2/(2*L*L)
def ground(z):return H*.5*(1-np.cos(math.pi*np.clip(z/L,0,1)))
feet={name:np.flatnonzero((sx*v[:,0]>0)&(sz*v[:,2]>0)&(v[:,1]<=.01)) for name,sx,sz in [('front_left',-1,1),('front_right',1,1),('rear_left',-1,-1),('rear_right',1,-1)]}
rows=[]
for r in j['rows']:
 w=v@np.array(r['matrix']).T;gap=w[:,1]+r['root_y']-ground(r['center']+w[:,2]);ff={}
 for name,ids in feet.items():
  i=int(ids[np.argmin(gap[ids])]);ff[name]={'minimum_gap':float(gap[i]),'contact_candidate_vertex':i}
 tail=np.flatnonzero(v[:,2]<-.30);body=np.flatnonzero((v[:,1]>.05)&(v[:,2]>=-.30))
 rows.append({'yaw':r['yaw'],'center':r['center'],'feet':ff,'max_individual_foot_gap':max(f['minimum_gap'] for f in ff.values()),'minimum_tail_clearance':float(gap[tail].min()),'minimum_body_above_5cm_clearance':float(gap[body].min()),'worldX_extent':float(np.ptp(w[:,0])),'worldZ_extent':float(np.ptp(w[:,2]))})
worst=max(rows,key=lambda r:r['max_individual_foot_gap']);summary={'worst_individual_foot':worst,'minimum_tail_clearance':min(r['minimum_tail_clearance'] for r in rows),'minimum_body_above_5cm_clearance':min(r['minimum_body_above_5cm_clearance'] for r in rows),'max_worldX_extent':max(r['worldX_extent'] for r in rows),'max_worldZ_extent':max(r['worldZ_extent'] for r in rows),'orientation_bisection_half_interval_rad':math.radians(40)/(2**25),'profile_C1_at_both_ports':True,'longitudinal_sample_spacing_m':(L+2)/128,'continuous_proof_claim':False}
result={'summary':summary,'rows':rows}
(p/'03-individual-feet-and-body-clearance.json').write_text(json.dumps(result,indent=2),encoding='utf8');result={'summary':summary}
