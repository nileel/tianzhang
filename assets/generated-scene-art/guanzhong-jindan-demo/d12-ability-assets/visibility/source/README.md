# 素材与制作来源

- 碑体来源：`src/Assets/Art/VFX/Guanzhong/Jindan/Space/Prefabs/ScaleSupport.prefab`，继续引用原 `Props/SupportStele` 的完整／失效网格与石材。没有新做或替换最终模型。
- 日／月纹样：本包原创 `PublicAltar.shader`，用圆盘、八向日芒和偏心圆相减的月牙表达，不依赖新增位图。`AltarGlyph.asset` 只是透明纹样载片，无碰撞，不是实体模型。
- 暖／冷微光：同 shader 的局部径向透明层，只作为公开坛 `Intact/Glow` 子节点；未创建指向隐藏人物的光源、粒子或轮廓。
- 棋子、地块、旧石材、灯光和远景沿正式 AdventureScene 及现有静态棋子 provider，只在独立资源场景中引用。原素材许可与制作记录沿用各自来源，不包含新下载的外部素材。
- 本包无 Imagegen 位图、无 Tripo 调用和点数消费；没有修改色彩空间、URP 资产、Depth/Opaque Texture 或 Renderer Feature。

公开坛识别沿已确认的 V2 显隐方案；本包资源与 Unity 运行截图不是方案重绘图。
