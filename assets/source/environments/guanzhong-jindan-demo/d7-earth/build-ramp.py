import bpy, math, pathlib, collections, json
source=pathlib.Path(r"D:/天章游戏开发/assets/source/environments/guanzhong-jindan-demo/d7-earth")
target=pathlib.Path(r"D:/天章游戏开发/src/Assets/Art/Environments/Guanzhong/EarthManifest")
source.mkdir(parents=True,exist_ok=True); target.mkdir(parents=True,exist_ok=True)
blend=source/"EarthManifestRamp.blend"; fbx=target/"EarthManifestRamp.fbx"
if blend.exists() or fbx.exists(): raise RuntimeError("D7 source already exists; inspect before replacing")
scene=bpy.data.scenes.new("D7_EarthManifestSource")
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1.0
vertices=[]; faces=[]; idx={}
def v(x,y,z):
    key=(round(-x,8),round(-z,8),round(y,8))
    if key not in idx: idx[key]=len(vertices); vertices.append(key)
    return idx[key]
def face(points): faces.append([v(*p) for p in reversed(points)])
# Logical centers stay fixed: the ramp occupies only the lower cell's strip,
# starting 0.10m after its center and ending at the shared edge 0.50m away.
n=80
for i in range(n):
    a=.1+.4*i/n; b=.1+.4*(i+1)/n
    ha=.75*(1-math.cos(math.pi*i/n))*.5; hb=.75*(1-math.cos(math.pi*(i+1)/n))*.5
    face([(-.25,ha,a),(-.25,hb,b),(.25,hb,b),(.25,ha,a)])
    face([(-.25,-.06,a),(-.25,-.06,b),(-.25,hb,b),(-.25,ha,a)])
    face([(.25,ha,a),(.25,hb,b),(.25,-.06,b),(.25,-.06,a)])
    face([(-.25,-.06,a),(.25,-.06,a),(.25,-.06,b),(-.25,-.06,b)])
face([(-.25,-.06,.1),(-.25,0,.1),(.25,0,.1),(.25,-.06,.1)])
face([(-.25,.75,.5),(-.25,-.06,.5),(.25,-.06,.5),(.25,.75,.5)])
mesh=bpy.data.meshes.new("EarthManifestRampMesh");mesh.from_pydata(vertices,[],faces);mesh.update()
obj=bpy.data.objects.new("EarthManifestRamp",mesh);scene.collection.objects.link(obj)
uv=mesh.uv_layers.new(name="UVMap")
for poly in mesh.polygons:
    for li in poly.loop_indices:
        co=mesh.vertices[mesh.loops[li].vertex_index].co
        uv.data[li].uv=(-co.x,-co.y)
mat=bpy.data.materials.new("D7Earth");mat.diffuse_color=(.27,.20,.12,1);mesh.materials.append(mat)
bpy.data.libraries.write(str(blend),{obj},fake_user=True,compress=True)
old=bpy.context.window.scene
try:
    bpy.context.window.scene=scene;obj.select_set(True);scene.view_layers[0].objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
        global_scale=1,apply_unit_scale=True,bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,
        bake_anim=False,path_mode='STRIP',use_custom_props=False)
finally: bpy.context.window.scene=old
mesh.calc_loop_triangles()
counts=collections.Counter(tuple(sorted((f[i],f[(i+1)%len(f)]))) for f in faces for i in range(len(f)))
result={'blend':str(blend),'fbx':str(fbx),'vertices':len(vertices),'triangles':len(mesh.loop_triangles),
'nonManifoldEdges':sum(c!=2 for c in counts.values()),'width':.5,'lowerCellStart':.1,'sharedEdgeEnd':.5,'rise':.75}
(source/"geometry.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
