# 长明资源来源

## 2026-11-17 灵火重做

本轮 `LowFire.shader` 的连贯曲线、弧形缺口、分层色块和脱落火舌均为项目自产解析HLSL；卡片网格仍仅是特效承载片，不是新实体模型。地表与中心共用该网格／材质，中心仍复用原支点碑。未新增位图、外部包或许可依赖。

风格参考为原作者 [Lush／Vefects 的风格化灵火展示](https://realtimevfx.com/t/free-stylized-fire-vfx-for-unreal-engine-and-unity/30352)，用于理解弯曲火舌与明暗分层，没有复制展示图或其代码。其 [Unity 商店资源](https://assetstore.unity.com/packages/vfx/stylized-fire-urp-355834) 于2026-11-17显示免费、42.8MB及Standard Unity Asset Store EULA；本项目未下载或纳入该包，不能把免费误记为CC0。既有余烬、蒸汽与水地表沿用下方原素材许可。

## 旧版来源记录

- `FireCenter` 直接派生自 `src/Assets/Art/Environments/Guanzhong/Props/SupportStele.prefab`，保留 `Intact/Body`、`Intact/Glow`、`Failed/Body` 原网格和变换。仅本包的微光材质变为暖色；长明毁中心按现有 `JindanFireDomainView` 语义移除碑，不额外留下封关断碑。
- `LowFire.shader` 与 `WarmTrace.shader` 是本包编写的局部 HLSL。低火只使用解析噪声、上行纹理坐标、锥形遮罩与暖色渐变，没有新增位图。六张交叉小卡片只承载特效，最高 0.34 米，无实体碰撞；台缘窄带沿正式六角足迹放置，不移动地块与格心，不表达局部圆形领域。
- 余烬材质派生 D12.1 的 `Earth/Materials/SoilDust.mat`，直接引用其 `smoke_03.png`。原始素材为 Kenney Particle Pack 1.1，CC0 许可保存在 [前包源目录](../../earth/source/Kenney-CC0.txt)，原包位置见 `assets/combat-vfx/kenney-particle-pack/README.md`。没有重新下载或复制贴图。
- 灭火蒸汽直接引用 `Water/Prefabs/ExtinguishSteam.prefab`；湿、浅、深地表直接引用对应 Water Prefab，原纹理及 Unity Companion License 继续保存在 [水包来源](../../water/source/README.md)。自然焦地派生原 `Waterbank/WaterHex.prefab` 与 `MudBank.mat`。
- `FireSampleSupport` 为 D12.2 `WaterSampleSupport` 的包内派生副本，保持同样的台阶条带裁剪与坡面12等分。原类为 internal；本包不改变前包 API、正式场景与共享贴面行为。

未新增模型、Imagegen 位图、Tripo 消费、VFX Graph、NOVA、屏幕折射或 Renderer Feature。V2/V3 图仅作为设计依据，Unity 样例及取证才是本包资源证据。
