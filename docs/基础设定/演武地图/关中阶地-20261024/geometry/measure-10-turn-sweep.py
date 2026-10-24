import bpy,numpy as np,math,json
from pathlib import Path
s=bpy.data.scenes['D1_Measure_20261024'];o=next(o for o in s.objects if o.type=='MESH' and o.name.startswith('D1_Beast_'));v=np.array([list(x.co) for x in o.data.vertices])*min(1.8/.99951172,.9/.45556642)
# D1 flat 60-degree vertex sweep around logical front-cell pivot.
px=v[:,0];pz=v[:,2]-.5; mins=np.array([np.inf,np.inf]); maxs=-mins
for deg in np.linspace(0,60,601):
 a=math.radians(deg);xy=np.column_stack((px*math.cos(a)+pz*math.sin(a),-px*math.sin(a)+pz*math.cos(a)));mins=np.minimum(mins,xy.min(0));maxs=np.maximum(maxs,xy.max(0))
result={'sweep_degrees':[0,60],'angle_step_degrees':.1,'pivot':'front logical cell center; mesh origin starts .5m behind pivot','measured_actual_vertex_bounds_x_axial_min':mins.tolist(),'measured_actual_vertex_bounds_x_axial_max':maxs.tolist(),'sampled_swept_rectangle_size':(maxs-mins).tolist(),'angle_sampling_bound_m':float(np.sqrt(px*px+pz*pz).max()*math.radians(.1)/2),'clear_circle_diameter_with_5cm_per_side':float(np.sqrt(px*px+pz*pz).max()*2+.1),'note':'Circle is sufficient conservative 360-degree clearance, not a required/new gameplay footprint; pair occupancy remains two cells.'}
p=Path(r'D:\Temp\TianZhang-Blender\d1-approved-20261024');(p/'10-turn-swept-actual-vertices.json').write_text(json.dumps(result,indent=2),encoding='utf8')