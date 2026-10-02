# 两个通用战斗棋子底座：造型与生产合同

状态：**供负责人选择与交易前批准的设计方案**。本文件不代表 Tripo 已下单、模型已产生或 Unity 已接入。

## 目的与适用范围

两款底座保持为角色 Prefab 之外的独立表现资源：第一款服务双足人形，第二款服务四足／宽足印生物。这是足印适配建议，**不是**已批准的阵营、种族或职业分类。两款都须由后续唯一的 `Static3DCombatUnitPresentationProvider` 消费，不能形成第二个底座管理器或回写战斗规则。

预览：[`base-01.png`](../../../assets/generated-scene-art/combat-bases/concepts/base-01.png) 与 [`base-02.png`](../../../assets/generated-scene-art/combat-bases/concepts/base-02.png)。预览只确认轮廓、材质方向和“中央留空”；可生产尺寸以本合同为准。

| 候选 | 足印语义 | 轮廓与视觉方向 | 为什么保留 |
| --- | --- | --- | --- |
| Base 01 — 圆形双足台 | 站姿集中在中心的双足人形 | 直径 0.62 m 的低圆台；灰色石质中央顶面、青玉色六分外环与低调青铜包边 | 与现有角色水平包络（`X/Z=±0.30 m`）相配，六向外环不碰中央可达高光。 |
| Base 02 — 圆角矩形四足台 | 四足／前后跨度更大的生物 | `X=0.96 m`、`Z=1.18 m` 的圆角矩形低台；深色石质中央顶面、琥珀色六分外环与青铜包边 | 为现有石甲兽 `X=±0.42 m`、`Z=-0.48..0.58 m` 留出每侧至少 0.06 m，不借缩放掩盖足印差异。 |

## 共同几何、坐标与表现层

所有度量均为米，适用资产根节点。每款底座的根位于顶面投影中心 `(0,0,0)`，顶面为局部 `Y=0`，底面为 `Y=-0.04`；厚度固定 `0.04 m`。角色双足／四足的承重面仍为自己的局部 `Y=0`，故脚底恰好落在底座顶面，不能为底座另加角色偏移。资产坐标固定为 `+Y` 上、局部 `+Z` 前，根的 Position/Rotation/Scale 均为 `(0,0,0)/(0,0,0)/(1,1,1)`；六向仅由表现根绕 Y 旋转现有 `90/150/210/270/330/30` 度。

两款均为封闭静态网格：下缘和顶缘各有小倒角，中央平顶不得刻字、浮雕、尖刺或高边。外围一圈才可有六段槽线；槽线的分段是方向读感，不能成为游戏朝向或阵营数据。视觉按三个独立层实现：

1. `BaseGeometry`：中性石材与非发光青铜边框；不携带阵营色、选中状态或规则字段。
2. `FactionTintBand`：仅为最外侧凹槽／窄环的可着色材质槽，接线时承接现有玩家青色、敌人红色提示；中央顶面保持中性。
3. `SelectionHighlight`：由后续表现层独立绘制的薄轮廓／半透明覆盖，位于顶面上方不高于 `Y=0.003 m`，遵循相同足印。它必须能显示可达、选中与移动反馈，不能被图案或实心饰件遮住。

这与当前 provider 创建 `Cylinder`（局部中心 `Y=-0.04`、`scale.y=0.08`）的世界厚度 `0.08 m` 区分开：新资源的物理厚度按本合同为 `0.04 m`，接入卡须在已有底座减薄与接地修正完成后一次替换，不能叠加缩放或 offset 补丁。

## 生产输入与候选上限

生产前需同时取得两项人工回执：选择 Base 01/02 的造型方向，以及独立的 Tripo 交易前授权。历史男主／石甲兽 credits 不可复用。每一款资源由自己的 A 卡完成，彼此不混用候选或费用。

| 项目 | Base 01（A-GZ-BASE-MODEL-01） | Base 02（A-GZ-BASE-MODEL-02） |
| --- | --- | --- |
| 唯一视觉输入 | `assets/generated-scene-art/combat-bases/concepts/base-01.png`，SHA-256 由下单前 manifest 记录 | `assets/generated-scene-art/combat-bases/concepts/base-02.png`，SHA-256 由下单前 manifest 记录 |
| 数量与候选 | 1 次 image-to-model，最多 1 个原始候选；失败不自动重试 | 1 次 image-to-model，最多 1 个原始候选；失败不自动重试 |
| 可核实 Tripo 选项 | H3.1 `v3/generation/image-to-model`，`texture=false`、`pbr=false`、`geometry_quality=standard`、`smart_low_poly=true`、`quad=true`、`face_limit=5000`；固定记录 `model_seed`，不启用 parts、rig、retarget 或自动补全 | 与 Base 01 相同，但提示词明确为圆角矩形、`X=0.96 m`、`Z=1.18 m` 的四足足印 |
| 下单文字约束 | 低圆台、平顶、无角色／文字／环境、六段外环只作凹槽、根在顶面中心 | 低圆角矩形台、平顶、无角色／文字／环境、六段外环只作凹槽、根在顶面中心 |
| 单款上限 | 35 credits：H3 image-to-model 无贴图 20 + `smart_low_poly` 10 + `quad` 5 | 35 credits：同左 |

因此两款生成交易总上限为 **70 credits（按官方 `1 credit = US$0.01` 即 US$0.70）**；不包含未授权的贴图、重试、转换、分件、rig 或平台订阅。价格与 H3.1 参数在下单当日重新核验；任务返回的 `credits_consumed` 是唯一实际费用记录。Tripo 的 H3.1 文档列出 stable `v3.1-20260211`、`quad` 与 `smart_low_poly` 参数；官方价目列出 H2/H3 无贴图 image-to-model 20 credits、`smart_low_poly` 10、`quad` 5，以及 `US$1=100 credits`。来源：[Tripo H3.1](https://developers.tripo3d.ai/en/models/v3-1)、[Tripo Pricing](https://docs.tripo3d.ai/get-started/pricing.html)。

下单后的本地清理仍属于各 A 卡：仅移除非网格杂项、按本合同精确缩放和根轴整理、导出一次 FBX，并以 factory-empty Blender 回读记录接地／轴向／网格／哈希。生成图不等于可接入模型，未获回执不得调用 Tripo。

## 下游冻结与未解锁项

- `A-GZ-BASE-MODEL-01`、`A-GZ-BASE-MODEL-02` 消费各自单一图像与参数；本轮只补齐其生产路径、完成条件和费用门，仍保持 blocked。
- `U-GZ-BASE-MODELS-01` 只能在两份清理资产和 `U-GZ-PLAYTEST-BASE-FIT-01` 都完成后，替换临时 Cylinder，并以 Unity Game View／运行时取证两种足印、六向、阵营带和选中高光。
- 没有改变角色现有独立资源、正式战斗规则、数值、骨骼、动画或全局 UI；最终正式入口重测仍由 `V-GZ-ART-SLICE-01` 负责。
