# D12.2 源素材

2026-11-16，采用工程已有素材，无新位图生成、无 Tripo 消费。

| 文件 | 原位置 | 采用方式 |
|---|---|---|
| `foam_detail_tiling.png` | Unity Shader Graph 17.3.0 `Samples~/ProductionReady/Environment/Water/foam_detail_tiling.png` | 水面细纹及低潮头泡沫，原文件复制 |
| `puddle_norm.png` | 同包 `Samples~/ProductionReady/Weather/Rain/Textures/puddle_norm.png` | 两次不同速度滚动取样，原文件复制；Unity 导入为 Normal Map |
| Earth 包的 `Textures/smoke_03.png` | 仓库已有 Kenney Particle Pack 1.1 | 蒸汽直接引用，不重复导入；[CC0 许可](../../earth/source/Kenney-CC0.txt) |

本机包根为 `src/Library/PackageCache/com.unity.shadergraph@2b401d56d4b3/`，只读复制，不修改 PackageCache。采用位图也保存在本目录，SHA-256 见 [texture-sha256.json](texture-sha256.json)。

官方纹理依 [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) 用于本 Unity 项目，原包许可声明见 [Unity-Companion-License-notice.md](Unity-Companion-License-notice.md)。技术来源：[官方水样例](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/Shader-Graph-Sample-Production-Ready-Water.html)、[官方贴花样例](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/Shader-Graph-Sample-Production-Ready-Decal.html)。

检查过 `WaterSimple_FoamMask`、`WaterStream`、`DecalWaterWetness` 的当前本地原件。前两者依赖 SceneColor／SceneDepth，FoamMask 还带 Gerstner 顶点位移；后者是 Decal SubTarget。当前工程的 Depth／Opaque Texture 关闭，Renderer Features 为空，因此不导入整图、不添加渲染功能。只复用泡沫、法线和滚动取样方法，用本包 HLSL 材质显示固定支承面；不复制样例海面范围遮罩。

水片直接引用 `Assets/Art/Environments/Guanzhong/Waterbank/WaterHex.prefab` 的原 FBX 网格；低植直接引用 `Assets/Art/Environments/Guanzhong/Props/LowPlant.prefab` 的 `Living` 网格。湿泥基色来自 `WetShore.mat`，露干阶段来自 `MudBank.mat`，浅水来自 `Water.mat`；原件均保留。

低植消退复用 Earth 包的 `EarthManifest.shader`，源纹理仍引用原植物材质；动画仅写材质 `_Progress`，不缩放／替换模型。蒸汽材质从 Earth 的 `SoilDust.mat` 派生，粒子改为低密度淡冷蒸汽。
