# 战术角色 2D 锚点与静态 3D 变换修正设计

日期：2026-08-23
状态：方案 A 已由负责人批准并实现；等待 Codex 独立复审
适用范围：`U-CHAR-2D-TACTICAL-PROTO-01`、苻渊静态 3D 并行对照及其固定比较证据

## 1. 问题与根因

### 1.1 2D 脚底锚点

六张 768×768 战术精灵目前由 `VisualBaselineBuilder.ConfigureTacticalSpriteImporters` 固定为 512 PPU 和 `spritePivot=(0.5,0.125)`，但 Builder 没有同时固定 `spriteAlignment=Custom`。现行合同也把共同脚底线写成 `y=96 px`，与实际透明主体的接地位置及负责人在 Unity Inspector 中验证的结果不一致。

因此，失败截图中的主体位置不是镜头、地块、排序或角色根节点问题，而是 Sprite Importer 的持久化锚点合同错误：脚底没有成为 Unity 实际使用的共同根锚点，遮挡探针也就不能在既有格位上形成预期的下半身遮挡。负责人已在六张 Unity `.meta` 中验证 `Sprite Alignment=Custom`、归一化 Pivot Y=`0.18` 为正确方向。

### 1.2 静态 3D 导入轴

`VisualBaselineBuilder.BuildStaticChessAssets` 将导入的 FBX 实例命名为 `FuYuan_Model`，随后强制设置 `localRotation=identity`。实际导入后的可见网格轴向与该身份旋转不匹配，所以模型在固定测试台上横躺。负责人在 Unity 运行时把该子节点本地 Rotation 调整为 `(-90,0,0)` 后，模型能正确直立在地块上。

六个 `FacingProbe` 的规则朝向仍由各 Prefab 根节点的 Y 旋转 `90/150/210/270/330/30` 负责。`FuYuan_Model` 的统一 X 轴修正只适配 FBX 到 Unity 的可见轴，不重排方向、不修改规则 Facing，也不是单个探针的私有补偿。

### 1.3 独立底座位置

Builder 当前把 `StaticChessBase` 放在本地 `(0,-0.04,0)`。负责人确认目标改为本地 Position `(0,0,0)`，现有 Rotation `(0,0,0)` 和 Scale `(0.66,0.04,0.66)` 保持不变。该值必须由静态棋子 Prefab 的生成入口统一持久化，不能依赖运行时场景 override。

## 2. 目标与非目标

### 目标

- 六张 2D Sprite 都确定性使用 `Sprite Alignment=Custom`、Pivot `(0.5,0.18)`、512 PPU。
- `FuYuan_Model` 在唯一静态棋子 Prefab 中统一使用本地 Position `(0,0,0)`、Rotation `(-90,0,0)`、Scale `(1,1,1)`。
- `StaticChessBase` 统一使用本地 Position `(0,0,0)`、Rotation `(0,0,0)`、Scale `(0.66,0.04,0.66)`。
- `AdventureScene` 的全部六向实例由同一 Prefab 和现有 Builder 重建，不保留方向私有 override。
- 修正合同、任务卡、验证记录和测试中的旧值，使后续重建不会把负责人已验证的设置改回去。
- 在固定 1920×1080、1× Game 视图重新证明 2D 接地／下半身遮挡和静态 3D 直立／底座位置。

### 非目标

- 不修改六张源 PNG 或其 Unity 副本的像素、尺寸、哈希、方向顺序与 PPU。
- 不修改冻结的 FBX、Blender 文件、模型网格、材质或 ModelImporter 的轴转换选项。
- 不修改镜头、六角地块、遮挡柱、角色根节点格位、六向 Y 旋转、sorting layer 或 render queue。
- 不接入正式单位、规则、占格、战斗结算、AI 或存档链。
- 不新增兼容分支、运行时修正脚本、按方向偏移表或第三条视觉路线。

## 3. 方案选择

采用已批准的方案 A：在现有 Unity 确定性 Builder 中固化资产级变换。

未采用的方案：

- 修改 `ModelImporter.bakeAxisConversion`：会触发 FBX 坐标数据重导入并扩大对材质、包围盒和方向证据的影响，不符合当前最小修正。
- 重新进入 Blender 导出：冻结 FBX 本身没有结构性问题，Unity 子节点 `-90° X` 已直接证明可见轴适配足够，因此不修改来源资产。
- 保存场景实例 override：会让六个方向或某个临时运行时实例成为变换事实源，无法承受 Builder 重建，也违反单一 Prefab 合同。

## 4. 所有者与数据流

### 4.1 2D

数据流保持为：批准 PNG → Unity 副本及 `.meta` → `VisualBaselineBuilder.ConfigureTacticalSpriteImporters` → `FuYuan_TacticalSprite.prefab` → `AdventureScene/TacticalSpriteProbeGroup` → `TacticalSpritePresentationController`。

- 资源事实：六张 PNG 的内容、哈希和方向编号。
- 导入事实：Builder 统一写入 `SpriteImportMode.Single`、512 PPU、`SpriteAlignment.Custom`、Pivot `(0.5,0.18)` 与透明 alpha。
- 运行时事实：控制器只选择方向 Sprite、对齐相机平面并消费既有表现事件；不重算或覆盖 pivot。

### 4.2 静态 3D

数据流保持为：冻结 FBX → `VisualBaselineBuilder.BuildStaticChessAssets` → `FuYuan_StaticChess.prefab` → `AdventureScene/FacingProbe_0..5` → `StaticChessPresentationController`。

- 模型来源事实仍是冻结 FBX；其字节与导入设置不变。
- Unity 集成事实由 Prefab Builder 持有：`FuYuan_Model` 的统一 `-90° X` 轴适配、底座零位置和双方既定缩放。
- 六向规则事实仍由探针根节点的 Y 旋转持有；表现控制器只变换角色根节点并在事件结束时复位，不改模型子节点或底座的局部变换。

## 5. 实施边界

### 5.1 确定性生成

在 `VisualBaselineBuilder` 中使用命名常量或同一逻辑单元内的明确固定值完成以下最小改动：

- Importer 判断条件同时比较 `spriteAlignment` 和 `spritePivot`；任一不匹配才重导入。
- 写入顺序明确设置 `spriteAlignment=Custom` 后再写 Pivot `(0.5,0.18)`。
- `FuYuan_Model.transform.localRotation=Quaternion.Euler(-90f,0f,0f)`。
- `StaticChessBase.transform.localPosition=Vector3.zero`。

随后只调用现有 Builder 重建战术 Sprite Prefab 与静态棋子 Prefab；`AdventureScene` 的六个实例继续继承同一静态 Prefab，无需制造场景 override 或重写 `.unity` YAML。不手写 `.prefab` 或 `.unity` YAML。

### 5.2 合同与状态

同步修正以下事实，避免代码、资源与管理状态互相矛盾：

- 2D 生产合同和 `prompts.md` 中的旧 Pivot／脚底锚点描述改为 `(0.5,0.18)`；保留 PNG 哈希、尺寸、方向和 PPU。
- 静态 3D 合同明确允许唯一 Prefab 子节点上的统一 Unity 导入轴适配；继续禁止按角色实例、探针方向、镜头或地块追加补偿。
- 独立占位底座的位置合同改为本地零位置，Rotation 与 Scale 保持既定值。
- `U-CHAR-2D-TACTICAL-PROTO-01` 的实施、验证、停止条件和 `expectedPaths` 纳入本次合同及 3D 对照修正所需路径；旧的 blocked 原因在新实机证据通过前只标记为待重新验证，不能提前宣称完成。
- 原验证记录保留上一轮失败证据，并追加本轮授权、变换改动、数值证据和新截图；不覆盖历史失败结论。

## 6. 测试与可见输出证明

### 6.1 EditMode

- 六张 Importer：`Sprite`、`Single`、512 PPU、`SpriteAlignment.Custom`、Pivot `(0.5,0.18)`、透明 alpha。
- 六张实际 `Sprite.pivot` 与 768×768 画布换算一致，并且六向相同。
- 静态 Prefab：根节点单位变换；`FuYuan_Model` 本地 Position 为零、Rotation 等价于 `(-90,0,0)`、Scale 为一；`StaticChessBase` 本地 Position/Rotation 为零、Scale 为 `(0.66,0.04,0.66)`。
- `AdventureScene` 六个 `FacingProbe` 仍来自同一 Prefab，规则朝向和 `HexCoord.Directions` 不变；不存在六向各自的模型／底座变换差异。
- PNG 与批准来源的 SHA-256 保持逐张相同，FBX 哈希保持不变。

### 6.2 PlayMode 与数值证明

- 2D：记录每个探针的活动 Sprite 名、Importer pivot、运行时 `Sprite.pivot`、`SpriteRenderer.bounds.min.y/max.y`、角色根位置以及遮挡探针与遮挡物的深度关系。
- 3D：记录每个探针的 `FuYuan_Model.localRotation`、合并 Renderer bounds 的 `min.y/max.y/height`、底座局部变换和底座 bounds。
- 六类表现事件结束后，角色根节点继续复位；2D 子 Sprite 和 3D 模型／底座局部变换不得漂移。
- 固定 1920×1080、1× Game 视图分别截取只启用 2D 和只启用 3D 的证据：2D 必须可见脚底接地和只遮住下半身；3D 必须直立且底座位置符合批准结果。

### 6.3 项目检查

实施后运行现有 Unity EditMode、PlayMode、资产版本、数据链、程序集边界和审核文本检查，并在暂存前后运行 whitespace 与 cached diff 检查。相关输入未变化的无关检查不扩张。

## 7. 失败处理与停止条件

- Builder 重建后任何 Sprite 仍未使用 Custom Pivot，先停止并检查 Importer 持久化；不添加运行时偏移兜底。
- `-90° X` 后若六向规则正面不再与对应邻格一致，停止并分别核对模型子节点轴适配与根节点 Y 朝向；不叠加第二个旋转补丁。
- 底座零位置若在固定 Game 视图出现负责人未批准的明显穿插或悬空，只记录证据并请求新决策；不自动恢复 `Y=-0.04` 或修改模型高度。
- PNG／FBX 字节变化、需要修改镜头／PPU／地块／遮挡柱、需要逐方向变换或正式规则行为发生变化时立即停止。
- 新截图未形成可读的 2D 下半身遮挡或 3D 直立证据时，任务继续保持 blocked，不解锁视觉比较父项。

## 8. 完成条件

- Builder、Importer、两个 Prefab、场景实例、合同、任务卡、测试与验证记录对上述三个批准值完全一致。
- 全部自动化检查通过，PNG 与 FBX 来源不变。
- 固定视图同时给出可读的 2D 接地／遮挡和静态 3D 直立／底座证据。
- 只有取得上述证据并完成独立复审后，`U-CHAR-2D-TACTICAL-PROTO-01` 才解除 blocked，并允许 `A-CHAR-BATTLE-VISUAL-COMPARE-01` 继续。
