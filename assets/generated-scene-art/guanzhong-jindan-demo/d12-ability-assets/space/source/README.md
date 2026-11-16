# D12.4 素材与制作来源

2026-11-17。新增素材为本项目原创 HLSL 解析纹样与透明带状覆盖网格，不含第三方贴图、外部模型或新生成位图。未安装插件，未开启 Opaque Texture、Depth Texture 或 Renderer Feature。

- 支点统一复用 `src/Assets/Art/Environments/Guanzhong/Props/SupportStele.prefab` 的 `Intact/Body`、`Intact/Glow`、`Failed/Body`。原网格、纹理、比例、坐标和源资源的既有授权保持。包内只派生微光颜色和显隐状态。
- 微光从同目录 `SealGlow.mat` 派生；未修改共用 `VisualBaseline_Selected.mat`。
- `SpaceBand.shader`、`Meshes/SpaceBand.asset` 为本次原创。带网格只是 1 米长、0.44 米宽、24 段的平面透明覆盖层；没有墙体、传送门、碰撞或地形替换。
- 定尺用短／长横刻度，封关用断续外边框／内层单向箭头，关闭颜色微扰后仍保留不同形状。`_Shimmer` 只改变纹样亮度，不采样或扭曲背景。
- `Samples` 复用正式 Adventure 场景的144格阶地、灯光、相机和正式棋子资源；样例的生产规则来自现有 `JindanRehearsalFactory`、`CombatEntryAdapter`、`CombatCommandService`，无复制的规则结算。

资源继承与使用范围以原资产记录为准。本次没有从参考网页复制源码或图像，无新增外部素材授权事项。
