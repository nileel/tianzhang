# 苻渊关节人偶 · 穿臂宽袖与贴腿腰袍

[修订结构图](../../../generated-character-art/fuyuan-jointed-doll-cuff-20261105/concept/jointed-doll-assembly-concept-v3.png) · [走路装配图](working/assembly-walk.png) · [施法装配图](working/assembly-cast.png) · [Unity 实录与播放器](../../../../artifacts/fuyuan-jointed-doll-cuff-20261105/runtime/README.md)

## 本次制作

以 11 月 3 日完整肢体人偶为基础，按装配图统一独立配件和穿着后的扣位。袖筒从肘下包绕前臂，手从不对称的宽口伸出。炭黑外布和米白内衬随同一截前臂转动。原铜色腰扣压入衣料上缘，四片长袍在静止与普通动作中向腿侧收拢，蹬地时后片给鞋跟让位。

只重新生成单只袖筒，另一侧镜像。Tripo Studio H3.1，多视图、超清几何、4K、四边面目标 6000，一个候选。实扣 55 点，2460 → 2405，用户指定的余额下限为 2000 点。原始 FBX 为 5393 个顶点、5409 个面，一张 4096 × 4096 原色贴图。[四张输入和提示词](../../../generated-character-art/fuyuan-jointed-doll-cuff-20261105/sleeve/README.md) · [Tripo 原件](https://studio.tripo3d.ai/zh/workspace/generate/2247faf7-48eb-419a-a945-c65d54bb085e) · [生成页面截图](sleeve/tripo-result.png)

## 袖筒和四片腰袍

- 新袖筒的内壁和铜扣来自 Tripo 原网格。原件沿 Y 轴有贯通孔，装配时按实际孔心和前臂尺寸配准，保留原 UV 并镜像到双侧。袖筒整体绑定前臂，低手姿势给袖宽留出空间，双手举起施法时保持原姿。动作采用项目原有的 30fps。[原件测量](sleeve/raw-inspection.json) · [装配脚本](working/fit_sleeves.py)
- 装配后沿左前臂轴投影，贯通区可容纳约直径 128.7 毫米的圆。左右手张开后的投影宽度为 114.7、114.8 毫米。以 1.5 毫米间隔覆盖双手投影，打到手面的 2270／2349 条射线均可穿过相应袖筒。走路 48、施法 72、攻击 60 个整数帧逐帧检查，两袖与主体、四片腰袍及彼此均未检出网格相交。[贯通区](working/fitted-qa.json) · [逐帧检查脚本](working/inspect_contact.py) · [实测](working/sleeve-contact.json)
- 复用前、后侧腰片 Tripo 原网格、纹理和铜扣。前片最大横宽 320.0 → 234.6 毫米，后侧片 420.0 → 294.2 毫米。旧扣片比布边分别高出 64.6、76.0 毫米，新扣片顶部低于布边 15.0 毫米。前片膝高留量最大 4.5 毫米，走路时后片按同侧鞋跟位移最多多让出 7°。抽查 24 组片位，22 组下摆更贴腿，两处后脚蹬地时留出更多空间。[腰片修改](waist/apply_waist.py) · [下摆距离与穿出测量](waist/waist-qa.json)
- 腰袍走路 48 帧的四片正／背投影中，旧版有 25 个片位可见裤腿或鞋穿出超过 1 毫米，新版为 0。另核实施法与攻击各一个峰值帧。该检查测量指定方向可见的外表面。袖摆保留硬质关节人偶的随臂运动。

## 本机文件

可编辑的 [Blender](working/fuyuan-jointed-doll-cuff.blend) 和带动作／内嵌贴图的 [FBX](export/fuyuan-jointed-doll-cuff.fbx) 均为 7 个可换网格、23 根骨、4 个 4K 材质。二进制模型、[袖筒原包](sleeve/source.zip) 及播放器保存在本机。旧版工程保留在原位置。

| 文件 | SHA-256 |
| --- | --- |
| 新袖筒原包 | `d60b2639e3687ccb633bb52465f9d0632f10405248ee8a5beaff0fe2325c7cfa` |
| 可编辑 Blender | `0bdc793bec269aed8ecc3c5c71af15e4abd5145b26259992281f2ea7db73b64c` |
| 最终 30fps FBX | `71fd596616abaa8dc0623ecf6c62253f67e24ef6446c6f7ba67e18e2420c0200` |
