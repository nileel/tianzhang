D7 临时土坡源资产（2026-10-26）

EarthManifestRamp.blend 为对象库；在 Blender 中 Append → Object → EarthManifestRamp，或通过 bpy.data.libraries.load 读取。source-reopen.json 是保存后重新加载的324顶点／644三角／宽0.5米核对。
build-ramp.py 是构造与FBX导出源，使用现有 blender_lab 会话执行；默认拒绝覆盖既有输出，请复现时改为新的输出目录。原常驻Blender场景的Cube/Camera/Light保留。
模型局部Unity +Z从低格朝高格，宽0.5米，坡段z=0.1～0.5，y=0～0.75；上沿落在共用边内，低／高格心不变。半余弦曲面80段，实体侧底面，封闭流形。Blender坐标经(-x,-z,y)及反绕序转换，FBX -Z Forward / Y Up、bake_space_transform。
Unity中的CombatEarthView按实际两端支承高差缩放Y（楼梯中台连接实际可为0.375米），CombatBattlefieldSurface使用同一曲面供脚底、路径和拾取；不以整数heightLevel代替真实表面高度。
只允许单格通过；单格人物沿临时陡坡纵向直立，并按真实两脚支承拟合，不把人物横躺贴坡。双格资格仍被空间净空1拒绝。
运行时材质复用Guanzhong/Ramp/Earth.mat。造墙复用Props/HighWall.prefab。本项未使用付费或外部生成服务。
