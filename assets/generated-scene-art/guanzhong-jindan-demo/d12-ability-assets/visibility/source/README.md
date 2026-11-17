# 素材与制作来源

- 碑体来源：`src/Assets/Art/VFX/Guanzhong/Jindan/Space/Prefabs/ScaleSupport.prefab`，继续引用原 `Props/SupportStele` 的完整／失效网格与石材。没有新做或替换最终模型。
- 日／月纹样：本包原创 `PublicAltar.shader`，用圆盘、八向日芒和偏心圆相减的月牙表达，不依赖新增位图。`AltarGlyph.asset` 只是透明纹样载片，无碰撞，不是实体模型。
- 暖／冷微光：同 shader 的局部径向透明层，只作为公开坛 `Intact/Glow` 子节点；未创建指向隐藏人物的光源、粒子或轮廓。
- 全棋盘范围：原创 `DomainRange.shader` 采用连续世界XZ噪声、滚动光带及短时展开亮度，不再画逐格边框。`WholeBoardRange.asset` 为固定地图VFX载片，并作原生粒子的发射面，不是实体模型。贴面工具 `VisibilityRangeSupport` 是现有 `FireSampleSupport` 的本包副本，继续使用12等分曲坡和D3.3石阶条带裁剪；未修改Fire／Water工具或公共接口。
- 日光、光尘、低雾和月光：复用 `Earth/Textures/smoke_03.png` 与 `trace_01.png`；本包四个材质分别派生自 `Earth/Materials/SoilDust.mat`、`PressureTrace.mat`，不修改原材质／纹理。来源为Kenney Particle Pack 1.1，CC0，沿用[现存许可](../../earth/source/Kenney-CC0.txt)。没有新下载或生成位图。
- 动效方向经用户确认：日光参考 [Cyanilux God Rays](https://www.cyanilux.com/tutorials/god-rays-shader-breakdown/)，低雾参考 [Oliver McDonald 的 Akali Twilight Shroud](https://beardilocks.artstation.com/projects/eamme3)；只借鉴形态与运动分层，没有复制参考作品的素材或代码。
- 棋子、地块、旧石材、灯光和远景沿正式 AdventureScene 及现有静态棋子 provider，只在独立资源场景中引用。原素材许可与制作记录沿用各自来源，不包含新下载的外部素材。
- 本包无 Imagegen 位图、无 Tripo 调用和点数消费；没有修改色彩空间、URP 资产、Depth/Opaque Texture 或 Renderer Feature。

公开坛识别沿已确认的 V2 显隐方案；按2026-11-17用户反馈补齐实际领域范围，并同步修改V2对应文字。本包资源与 Unity 运行截图不是方案重绘图。
