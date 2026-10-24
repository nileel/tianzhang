import bpy
from pathlib import Path
p=Path(globals().get('OUT_DIR',r'D:\Temp\TianZhang-Blender\d1-long-transition-20261024'))
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH'
scene.render.resolution_x=1600
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='OBJECT'
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD'
scene.world.color=(.08,.08,.08)
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(p/'07-actual-Blender-measurement-render.png')
bpy.ops.render.render(write_still=True)
result={'image':scene.render.filepath,'label':'Actual Blender Workbench camera render'}
