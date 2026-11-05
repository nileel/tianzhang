# 苻渊长袖轮廓修订

2026-11-05。用户否决上一版视频中短、鼓、外翘的袖件。本轮以[设计图右栏装配效果](../../../generated-character-art/fuyuan-jointed-doll-cuff-20261105/concept/jointed-doll-assembly-concept-v3.png)为目标，重生成自然下垂袖筒，保留先前已收窄的四片腰袍。

## 采用结果

- [可编辑 Blender](working/fuyuan-jointed-doll-silhouette.blend)
- [带动作和内嵌贴图的 FBX](export/fuyuan-jointed-doll-silhouette.fbx)，另存四张 4K [贴图](export/textures/)
- [Unity 播放器、同机位视频与设计对照](../../../../artifacts/fuyuan-jointed-doll-silhouette-20261105/runtime/README.md)
- [四视图与实际提示词](../../../generated-character-art/fuyuan-jointed-doll-silhouette-20261105/sleeve/README.md)

两只袖子均为独立可换件：前臂套入真实孔道，圆扣在布面上缘；黑色外布保留长斜口，米白内衬连续延伸到低尖角。低臂时袖高约 58.9 cm（2.12 倍前臂长），袖尖低于指尖约 22.3 cm（0.80 倍前臂长），与设计图的长袖比例接近。

袖口仍比插画圆滑、肘口厚圈更明显；这版保留 Tripo 布褶与纹理的细节差异。袖尾由骨骼控制限幅下垂，未制作布料物理。

## 来源与费用

先由 imagegen 制作同一资产的正、背、左、右四视图，再上传 Tripo H3.1；使用高分辨率几何、6K 四边面目标、4K 纹理。采用任务 `cd834ca9-0090-424c-b4ae-c1d1046fb1e9`，原始 FBX 实际为 5430 顶点／5397 多边形。保留[原始下载包](sleeve/source.zip)与[生成结果截图](sleeve/tripo-result.png)。

本轮仅付费生成一次，确认报价 55 点，余额 2405 → 2350 点。用户当次授权优先于默认单款 70 点确认规则，且要求余额不低于 2000；本轮未充值、未订阅。

旧版采用的水平兜状袖体，加上沿垂片方向的压缩与错误绕臂朝向，导致袖件短胖、像翅膀。本轮重新生成近竖直袖形；装配试验确认源轴无需额外 20° 偏转，否则布面会让主体穿出、圆扣转到后方。最终使用源 Z 轴直接适配前臂轴。

## 清理与装配记录

基础工程是上一版 `jointed-doll-cuff-20261105/working/fuyuan-jointed-doll-cuff.blend`，先从 `jointed-doll-20261103/working/fuyuan-jointed-doll.blend` 恢复三套动作中的上臂原始曲线，保留腰袍修改。原始新袖另存为隐藏对象 `LongSleeveRaw`，清理副本为 `LongSleeveClean`。

实际操作顺序：

1. [open_cuff.py](working/open_cuff.py) 的 `clean(scene, raw)` 修复微小裂缝。Tripo 肘口实际有封面，不能仅凭渲染中的黑色凹槽认为已经贯通。
2. [fit_sleeves.py](working/fit_sleeves.py) 的 `apply(scene, clean, (0, .024, .938), (.52, .66, .55))` 适配尺寸、镜像、面朝向及蒙皮。原材质 UV 随网格保留；布尔切口会新增局部切面，不能称为原网格逐点不变。
3. `open_passage(scene)` 用临时体积清理肘口至手部的内部封面。切割体在手指末端收束，避免切掉下方白衬；操作后删除临时体积，并给新增顶点重新写权重。
4. `allow_for_sleeve_width(scene, 4.0)` 按原动作顺序执行两次，低臂姿态合计约外开 8°。第二次根据已修改姿态重算权重，因此不能简单替换成一次固定 8°。
5. 最后 `animate_hanging(scene)` 重算现有两根袖骨。手部附近保留前臂权重，沿轴 0.84–1.05 m 平滑过渡到袖骨；袖骨方向使用随臂方向 55% 与斜向下方向 45% 混合。保存后按 30fps、逐帧烘焙、三角化、无附加叶骨导出。

最终袖骨重算后曾再次清理孔径并重建两只袖网格，使用同一参数；上臂与袖骨曲线未再次叠加修改。不要在成品 Blend 上重复运行让位函数，否则会累积开臂角度。

## 验证与边界

- [网格与比例](working/mesh-qa.json)：7 个蒙皮网格、23 根骨、99241 三角、4 个 4K 材质；walk 48 帧、cast 72 帧、attack 60 帧。
- [穿臂孔道](working/passage-qa.json)：将实际手掌轮廓以 1.5 mm 网格投影到肘口—腕口段，左 2230／右 2221 条射线均无阻挡。检测区间为静置轴 0.35–0.73 m，不把袖口外仍保留的长白衬算作内部封堵。
- [动作接触](working/contact-qa.json)：三动作 180 个整数帧中，两袖与身体、四片腰袍、另一只袖的三角形相交对数为 0。该结果不外推到其它动作、连续帧间、自相交或布料仿真。
- 角色主体原有非流形边保留；袖件仍有源模型微小边界，具体数值见网格报告。不作为封闭实体或打印模型。
- Unity 固定镜头画面与独立视觉复核记录保存在上述运行目录，最终交付以实际播放器中的袖长、白衬、扣位及动作表现为准。

原包、Blend、FBX、贴图和播放器二进制保存在本机；版本库登记四视图、脚本、来源记录、验证数据和精选对照图，不代表所有忽略的二进制已经远端备份。
