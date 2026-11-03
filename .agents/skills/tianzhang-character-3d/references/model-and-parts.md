# 3D 生成、网格拆件与分组贴图

## 从四视图生成

用同一资产的四个方向进行多视图生成。按实际槽位预览确认相机方向，避免误用四张图片的批量独立生成。对头／体两部件路线，每组四图单独生成，使用同一原画的全身比例进行最终合装。

Charakuru 1.1.1 的候选默认是 Smart Mesh P2.0、四边面、25,000、每部位 4 候选。这是版本配置，不能作为任意任务的消费授权或所有模型的最佳参数。执行时检查当前服务可用设置、任务用途和预算，使用足以暴露形状差异的最少候选。默认 4K 纹理，记录实际选项、报价和完成后的文件。

候选比较先看正侧背的脸型、发长、肩袖、手指、腰封和鞋，再看拓扑与贴图。分开生成的头、体各自裁切大小不代表头身比例。用原画及采用全身图校对位置与尺度，避免反复叠加 normalization 和 placement。

读取下载文件确认格式、尺寸、对象、材质与外部纹理。保存服务模型 ID、输入视图关联、导出预设和 SHA256。下载成功只能证明取得文件。候选采用由用户当前授权决定，Charakuru 原生候选确认阶段留在应用内供用户选择。

## 选择拆分方法

| 情况 | 方法 | 产物与检查 |
|---|---|---|
| 需要把脸、长发和衣服分别高质量生成 | 图像层先做头含发／身体四视图 | 输入边界明确，生成后仍需检查三维部件 |
| 网格已有断开的顶点连通分量 | 本地枚举、单独显示、语义归组 | 保留原面、UV、位置与相对关系 |
| 原模型很多部位粘连，需建立拆件基准 | 对采用模型做 Tripo 粗分割 | 分割结果作为新几何基准，检查轮廓与接口 |
| 局部形状本身不符合设计 | 用户授权范围内局部重生成或局部改形 | 新几何重新分析，对旧权重和 UV 的适用性重验 |
| 形状已采用，仅材质细节不足 | 先判断现有 UV 能否直接局部更新，再决定是否重展 UV | 原几何逐面对应，仅更新所需 UV／材质 |

独立连通分量是结构信息。小块可能是睫毛、纽扣、鞋底或布屑，不能只按体积、位置、连通数或名字决定语义。对部件编号逐个隔离显示，结合带贴图、线框、正侧背视图确认。

目标边界在连续面内部时，分析它连接的是服装结构还是误粘连。已有明确授权且切分方案可确定时，做最小局部切分并核对边界。需要重建隐藏面、改变衣型、付费重生成或存在多种外观取舍时，先给出该处图和具体方案再确认。不能用自动删面、按高度截断或忽略组件制造“拆完”的结果。

## 三种分组独立记录

1. 语义角色说明部件属于 head、hair 还是 body，服务于装配。
2. 贴图组决定哪些部件共用一套材质。
3. 物理组决定哪些区域共同运动、根部跟谁、末端如何摆。

Charakuru 的贴图接口使用下表固定 ID。独立工作流可以按目标引擎和材质预算选组数，这五组是本地包的接口约定。

| ID | 范围 |
|---|---|
| head | 脸、耳和头部皮肤 |
| hair | 完整头发，可含多个独立发束 |
| upper_body | 颈、躯干、臂、手及其衣服 |
| lower_body | 腰下衣装、袍裙／裤、腿和鞋，含小型腰饰 |
| accessories | 有独立处理意义的大型附件 |

在该接口内，head、upper_body、lower_body 必需，hair、accessories 按实际存在决定。所有组件恰好分配到一个贴图组；物理组只覆盖需要摆动的部分。袍摆和腿可以物理分开、贴图同组。袖衬和外袖应协同变形，不能因材质颜色不同就分配互不一致的运动。

使用 Charakuru 的五组接口时，头来源和身体来源不能混在同一贴图组。部件跨越腰部边界或两种来源都需要独立附件组时，先解决该处归属与边界，不能凭空增设第六个固定 ID。

合并贴图上传对象时保留各连通分量的坐标，合并对象不等于焊接所有顶点。对每个部件保留可追溯来源，使贴图返回后仍能恢复拆分与物理分组。

## 从实际几何制作重贴图参考

先从当前采用网格输出同机位正背面黏土／线框图。网格图控制轮廓、部件集合、孔洞、厚度、遮挡和接口，采用的彩色多视图控制花纹、色彩和材质。每组正背面各生成一张，不把纹理展开图或简单裁切当成完成参考。

```text
Create one standalone [front / back] orthographic illustration of
[the actual mesh group]. Image 1 is the actual mesh guide and defines its
silhouette, component set, garment boundaries, openings and relative positions.
Images 2 onward are the adopted painted views and define colors, patterns,
material finish and detail.

Retain every disconnected piece in its current relative position.
Replace wireframe, viewport UI and clay shading with finished materials.
Keep the exact cut, coverage and occlusion of the clothing.
Match the paired view's scale, framing and vertical baseline.
Use a transparent background and a complete silhouette.
Preserve [observed texture details], with neutral illumination and no floor
shadow, added body parts, invented accessories or extra seams.
```

下半身描述用完整着衣的腰下衣装、袍裙／裤、腿与鞋。衣物遮挡范围由网格参考决定，不能为了画清脚而脱掉袍子。头部、颈部、手和衣服边界的色彩需要相互连续。

## 已有模型只改局部贴图

先读最新源 Blend 和实际材质，确认目标是整组重贴图还是现有贴图中的局部区域。已有有效 UV 时，优先按原 UV 在作业副本中重绘目标区域、更新对应贴图或材质，展示模型上的接缝、花纹和颜色对照。长袍横跨腰线时可用面选择与 UV 遮罩限定修改范围，无需为了贴图分组切断衣型。

使用外部重贴图服务时先确认它是否保留现有 UV。确需重展时，只准备目标区域的临时副本，回收新 UV／材质到当前源网格并核对逐面对应，不让服务返回件替代已绑定模型。

Charakuru 1.1.1 的 production 命令有明确适用阶段。`analyze` 从采用候选／分割件开始，会清除当前分类；`prepare` 遍历全部贴图组并删除其已有返件；`returns`、`assemble` 也会重置绑定状态。完成件的局部贴图修复从最新 Blend 开始。已有完整物理设置的 springs 完成件，通过 `update-model.mjs` 的 `phase: "springs"` 登记修改后的 Blend／FBX并重新准备预览。该脚本的 rigged／weighted 阶段只登记 FBX，不读取 blend 参数；其它阶段须核实对应源文件登记入口。

尚未绑骨的 production 工程若已有有效 prepared 与其它组 returned，可复用它们，只更新目标组返件，再用全部有效返件装配。其他组缺少返件时，全流程 assemble 的条件并不成立，继续使用当前可编辑模型完成局部材质修改，核实合适的登记入口。若当前接口确实要求丢弃用户已有成果，先交付局部可检查结果并说明具体缺口，再确认是否扩大范围。

## 新建整套贴图时的 Smart UV 与原形回收

- 新建整套贴图使用本次准备好的组模型，先 Smart UV，再按对应正背面参考重贴图。已有 UV 的局部任务按上一节处理。Charakuru 返件要求每组一张 4096×4096 Base Color。Remove Lighting 按工程当前设置，1.1.1 初始为 OFF，不能仅凭名称判断能提高质量。
- 重贴图过程中保留拓扑，不执行重网格或减面。需要改变形状时回到几何基准阶段。
- 上传朝向与返件朝向分别验证。1.1.1 的 Y-up 烘焙 FBX 曾需在 Studio 设置 Y=90°，只对核实的导出条件应用一次。GLB 或已补偿文件先看实际预览，返件的 Blender XYZ 旋转单独确认。
- 返件可能因 UV 缝拆出重复顶点，也可能被服务改变比例或轴。按表面位置、面顶点集合及绕序验证对应，将 UV／材质写回原基准几何。
- 顶点数量相等不证明逐索引对应。对缺面、重面、反绕序、几何不匹配和歧义重合面，先检查来源与导出条件，不能只靠缩放通过。
- 重新生成的部件属于新几何，不能冒充重贴图返件。旧组件 ID 和物理范围按新网格重新对应。
- 合装后比较分割前后轮廓、接口和各部件相对位置，再检查纹理、透明度与目标材质显示。

Charakuru 的 `production.py` 在 1.1.1 中限制 Smart UV 上传为最多 80,000 三角面或 40,000 全四边面、150 MB。这是本地导出检查，不代表外部服务的永久上限。

## 已安装 Charakuru 时的命令

从安装根目录执行，配置路径采用绝对路径更清晰。使用前读取当前版本的 `skills/tripo-agent-workflow/references/commands.md`。下列是候选到装配的生产阶段入口，具有上节说明的状态重置效果；完成模型的局部修复使用当前源文件路线。

```text
node scripts/tripo-project.mjs CONFIG.json PROJECT.charakit
```

| action | 必要数据／作用 |
|---|---|
| candidates | source、name、fbx、resources，登记实际候选 |
| segmented | source、fbx，登记粗分割返件 |
| analyze | 枚举采用网格的连通部件 |
| classify | 完整 roles、textureGroups、springGroups |
| prepare | 全部组 FBX 与正背面网格参考，清除旧返件登记 |
| references | 组 id、view、png |
| returns | 组 id、fbx、resources、核实后的 rotation |
| assemble | 逐面核对并回收 UV／材质，保存装配结果 |

每个配置含 `input`，其相对路径以配置文件所在目录解析，输出指向同一正本。分类必须使用本次 inventory 的实际 ID，空分类示例不具备可执行意义。

粗分割准备使用独立后台 Blender，脚本会清空其执行场景。

```text
blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/export-segmentation.py -- PROJECT.charakit OUTPUT_DIR
```

得到采用候选的三角化 head.glb／body.glb 后，向 Studio 提交粗分割，核对返件再登记。已有独立组件足以完成任务时，可直接本地分析与归组。

标准 Studio 下载可使用包内 `skills/tripo-studio-download` 的 prepare／receive 入口，先读取它的当前参数。已有授权的服务连接也可按能力执行，但仍需核实模型 ID、实际文件与预设。保存骨骼时，VRM 骨架预设与 FBX 的 Blender 导出预设是两个不同选项。
