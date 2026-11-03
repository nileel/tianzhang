"""Complete the approved skirt lap edges from neighboring original painted faces."""
import bpy,bmesh,math
from mathutils import Vector
scene=bpy.context.scene;rig=bpy.data.objects['FuYuan_Puppet_Rig']
source_parts=[bpy.data.objects['Puppet_robe_'+str(n)] for n in range(6)]
for n in range(6):
    vertices=[];faces=[];uvs=[]
    for src in source_parts:
        layer=src.data.uv_layers.active.data
        for poly in src.data.polygons:
            x,y,z=poly.center
            angle=math.degrees(math.atan2(x,-y))%360
            delta=(angle-n*60+180)%360-180
            if not 29.9<abs(delta)<50:continue
            face=[];coords=[]
            for vi,li in zip(poly.vertices,poly.loop_indices):
                co=src.data.vertices[vi].co.copy()
                radial=Vector((co.x,co.y,0))
                if radial.length>0:co-=radial.normalized()*.007
                face.append(len(vertices));vertices.append(co);coords.append(layer[li].uv.copy())
            faces.append(face);uvs.append(coords)
    data=bpy.data.meshes.new('Robe_Lap_'+str(n));data.from_pydata(vertices,[],faces)
    for mat in source_parts[n].data.materials:data.materials.append(mat)
    layer=data.uv_layers.new(name='UVMap')
    for poly,coords in zip(data.polygons,uvs):
        poly.use_smooth=True
        for li,coord in zip(poly.loop_indices,coords):layer.data[li].uv=coord
    bm=bmesh.new();bm.from_mesh(data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
    bm.to_mesh(data);bm.free()
    obj=bpy.data.objects.new('Puppet_robe_'+str(n)+'_overlap',data);scene.collection.objects.link(obj)
    obj['part_role']='robe_'+str(n);obj['source']='approved lap edges copied from neighboring corrected Tripo faces'
    vg=obj.vertex_groups.new(name=obj['part_role']);vg.add(list(range(len(data.vertices))),1,'REPLACE')
    solid=obj.modifiers.new('Lap back','SOLIDIFY');solid.thickness=.003;solid.offset=-1;solid.use_even_offset=False;solid.material_offset=1;solid.material_offset_rim=1
    modifier=obj.modifiers.new('Rigid lap follows panel','ARMATURE');modifier.object=rig;obj.parent=rig
result={'added_overlaps':6,'radial_inset_m':.007,'sector_extension_degrees':20}
