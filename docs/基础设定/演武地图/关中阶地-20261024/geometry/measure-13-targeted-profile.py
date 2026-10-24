import bpy,numpy as np,math,json
from pathlib import Path
scene=bpy.data.scenes['D1_Measure_20261024']
o=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'))
v=np.array([list(x.co) for x in o.data.vertices])*min(1.8/.99951172,.9/.45556642)
L=1.125;H=.75;N=8;start=(1-L)/2;step=L/N;rise=H/N
foot_masks={}
for name,sx,sz in [('front_left',-1,1),('front_right',1,1),('rear_left',-1,-1),('rear_right',1,-1)]:
 foot_masks[name]=np.flatnonzero((sx*v[:,0]>0)&(sz*v[:,2]>0)&(v[:,1]<=.01))
def surface(x,kind):
 t=np.clip((x-start)/L,0,1)
 return H*t if kind=='ramp' else rise*np.floor(N*t+1e-9)
rows=[]
for kind in ['ramp','stairs']:
 for sign in [1,-1]:
  angle=sign*math.atan2(H,L);C=math.cos(angle);S=math.sin(angle)
  axial=C*v[:,2]-S*v[:,1];yy=S*v[:,2]+C*v[:,1];xx=.5+sign*axial
  zbase=.375;ground=surface(xx,kind);gap=yy+zbase-ground
  minimum=float(gap.min());lift=-minimum
  foot_rows=[]
  for name,ids in foot_masks.items():
   i=int(ids[np.argmin(gap[ids])])
   foot_rows.append({'name':name,'support_vertex_index':i,'local_xyz':v[i].tolist(),'world_axial':float(xx[i]),'support_height':float(ground[i]),'gap_before_vertical_fit':float(gap[i]),'gap_after_vertical_fit':float(gap[i]+lift),'low_sole_vertex_count':len(ids)})
  for yaw in [90,150,210,270,330,30]:
   r=math.radians(yaw);forward=np.array([math.sin(r),math.cos(r)]);side=np.array([math.cos(r),-math.sin(r)])
   points=axial[:,None]*forward+v[:,0,None]*side
   recovered=.5+sign*(points@forward);gs=surface(recovered,kind);g=yy+zbase+lift-gs
   rows.append({'kind':kind,'facing_relative_incline':sign,'yaw':yaw,'pitch_deg':math.degrees(angle),'root_y_relative_low':zbase+lift,'vertical_fit_m':lift,'min_gap_all_vertices_after_fit':float(g.min()),'vertices_below_minus_2mm':int((g<-.002).sum()),'feet':foot_rows,'max_gap_among_four_foot_supports':max(f['gap_after_vertical_fit'] for f in foot_rows),'all_vertex_min_before_fit':minimum,'vertical_fit_is_contact_envelope_not_approved_runtime_rule':True})
result={'parameters':{'L':L,'H':H,'N':N,'start_from_low_cell':start,'end_from_low_cell':start+L,'step_run':step,'step_rise':rise,'foot_anchor_mean_distance':.6762090856223276,'tilted_anchor_horizontal_distance':.6762090856223276*math.cos(math.atan2(H,L))},'scope':'D1 single requested analytic profile trial only. All original mesh vertices; rigid body rotation and minimum vertical translation; unchanged meshes. Foot support candidates are actual lower sole vertices <=1cm, not bbox minima. No triangles or actual assembled asset acceptance.', 'center_stationary_rows':rows}
Path(r'D:\Temp\TianZhang-Blender\d1-approved-20261024\13-L1125-8step-targeted-profile.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'proper_yaw_rotation_for_both_facing_signs':True,'six_yaw_min_gap':min(r['min_gap_all_vertices_after_fit'] for r in rows),'max_four_foot_gap':max(r['max_gap_among_four_foot_supports'] for r in rows)}
