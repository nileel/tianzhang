import bpy,numpy as np,math,json
from pathlib import Path
o=bpy.data.objects['D1_Player_FormalPlayer_Static3D_Mesh']
v=np.array([list(x.co) for x in o.data.vertices])*1.4
edges=np.array([list(e.vertices) for e in o.data.edges])
H=.75;N=8;rise=H/N;port_span=math.sqrt(3);start=(port_span-1.125)/2;middle_depth=.5;flight_run=.3125;run=flight_run/4;L=2*flight_run+middle_depth;upper_start=port_span/2+middle_depth/2
boundaries=np.r_[start+np.arange(1,5)*run,upper_start+np.arange(1,5)*run]
def terrain(z):
 return rise*(np.floor(4*np.clip((z-start)/flight_run,0,1)+1e-8)+np.floor(4*np.clip((z-upper_start)/flight_run,0,1)+1e-8))
def evaluate(center,yaw):
 s,c=math.sin(yaw),math.cos(yaw);mat=np.array([[c,0,s],[0,1,0],[-s,0,c]])
 w=v@mat.T;z0=w[edges[:,0],2]+center;z1=w[edges[:,1],2]+center
 pp=[v];zz=[w[:,2]+center];yy=[w[:,1]]
 for b in boundaries:
  take=(np.minimum(z0,z1)<b)&(np.maximum(z0,z1)>b)&(abs(z1-z0)>1e-12)
  ed=edges[take];tt=(b-z0[take])/(z1[take]-z0[take]);p=v[ed[:,0]]+tt[:,None]*(v[ed[:,1]]-v[ed[:,0]])
  pp.append(p);zz.append(np.full(len(p),b));yy.append(p[:,1])
 pp=np.concatenate(pp);zz=np.concatenate(zz);yy=np.concatenate(yy);req=terrain(zz)-yy
 ai=int(np.argmax(req));root=float(req[ai]);soles=(pp[:,1]<=.01);left=np.flatnonzero(soles&(pp[:,0]<0));right=np.flatnonzero(soles&(pp[:,0]>=0))
 il=int(left[np.argmax(req[left])]);ir=int(right[np.argmax(req[right])])
 return {'yaw':math.degrees(yaw),'center_z':float(center),'root_y':root,'support_is_low_sole':bool(soles[ai]),'support_local':pp[ai].tolist(),'source':'vertex' if ai<len(v) else 'triangle_edge_at_riser','left_gap':float(root-req[il]),'right_gap':float(root-req[ir]),'left_contact_local':pp[il].tolist(),'right_contact_local':pp[ir].tolist(),'full_triangle_min_gap':float(root-req.max()),'exact_clipped_points':len(pp)}
rows=[];stationary=[];extents=[]
for yawdeg in [90,150,210,270,330,30]:
 yaw=math.radians(yawdeg);s,c=math.sin(yaw),math.cos(yaw);z=-s*v[:,0]+c*v[:,2];sole=v[:,1]<=.01
 extents.append({'yaw':yawdeg,'sole_z_min':float(z[sole].min()),'sole_z_max':float(z[sole].max()),'whole_z_min':float(z.min()),'whole_z_max':float(z.max()),'lower_riser_clearance_from_whole_mesh':float(boundaries[0]-z.max()),'upper_last_riser_clearance_from_whole_mesh':float(port_span+z.min()-boundaries[-1])})
 for center in np.linspace(-.25,port_span+.25,161):rows.append(evaluate(center,yaw))
 for center in [0,port_span/2,port_span]:stationary.append(evaluate(center,yaw))
summary={'H_m':H,'stair_run_m':L,'risers':N,'riser_height_m':rise,'tread_depth_m':run,'riser_z_positions_m':boundaries.tolist(),'port_center_span_m':port_span,'ports':[0,port_span/2,port_span],'middle_platform_depth_m':middle_depth,'flight_run_m':flight_run,'flight_count':2,'risers_each_flight':4,'horizontal_platform_center_to_stair_domain_m':start,'first_riser_from_lower_center_m':float(boundaries[0]),'last_riser_to_upper_center_m':float(port_span-boundaries[-1]),'stop_rule':'All three center rows stop on actual horizontal platforms at Y=0,.375,.75. No transit-only state. Stair profile uniquely replaces Tile inside footprint.','poses':len(rows),'non_sole_limiting':sum(not r['support_is_low_sole'] for r in rows),'max_other_foot_gap_during_move_m':max(max(r['left_gap'],r['right_gap']) for r in rows),'stationary_max_foot_gap_m':max(max(r['left_gap'],r['right_gap']) for r in stationary),'max_root_height_jump_between_samples_m':max(abs(rows[i+1]['root_y']-rows[i]['root_y']) for i in range(len(rows)-1) if rows[i]['yaw']==rows[i+1]['yaw']),'body_orientation':'upright, pitch=roll=0, six yaw preserved','extents':extents,'minimum_clear_width_m':1.0,'geometry_surface_proof':'At each sampled pose, all original triangles clipped exactly at all vertical risers; vertex + triangle-edge intersections give exact piecewise constant solid envelope. No macro-slope collider.','motion_scope':'All three standing platform centers verified. Travel poses are geometric support envelope, not animated stepping; root may rise up to one riser between consecutive states. D3 must animate step clearance using real treads, with no static mid-stair floating.'}
result={'summary':summary,'stationary':stationary,'rows':rows}
(Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))/'13-upright-player-stairs-middle-platform.json').write_text(json.dumps(result,indent=2),encoding='utf8')
result={'summary':summary}
