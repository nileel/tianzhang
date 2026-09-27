# 石甲兽四视图与Tripo重制合同

> 2026-09-27最新用户修订：双方按截图使用Tripo智能网格P2.0、四视图、四边面、统一25000面、数量4，各一批最多100credits；此参数替代本文旧单候选/H3.1/额度约束。一批4个候选全部留证，用户选定其中一份后才进入原包清理，禁止批次外重试。原画身份、输入字节、静态用途与后续真实品质验收保持。

2026-09-27用户明确指定重做石甲兽，采用标准四视图→Tripo流程。该指令替代8月27日旧合同对石甲兽的Blender原生生产路线；旧v1及AI自签批准已经撤销，只作失败证据。本合同不改变男主任务或批准额度。

## 唯一造型输入
- 使用9月20日用户已通过的概念原画：assets/generated-character-art/no-title/shijiahou_concept_v1.png（SHA-256：2f8d193f5303162ad4cef54f6ba9b4eab2944e148c77322040c083eab62ac095）。原画说明中的用户批准只适用于概念方向。
- enemy_shijiahou：有机重型四足、楔形兽头、宽厚躯干、板岩式层叠石甲、可见皮肤关节、四个承重足及分节渐细尾。低饱和哑光灰褐，不做积木石堆、机甲、人形、发光裂缝、武器或特效。
- 保留combat_enemy_shijiahou_v1稳定Profile；source转入assets/source/characters/combat-pieces/shijiahou-static-3d-v2/，旧v1不覆盖。先产可判断的实际候选；没有具体图像不请求空泛美术批准。

## 一次性完整任务拓扑
1. A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-FOURVIEW-01：四张独立正交PNG＋manifest＋用户本人批准。
2. A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-AI-RAW-01：前置为四图通过及该资产独立交易批准；一项真实Tripo多视图静态生成，原包/平台记录/多角度证据及用户原包批准。
3. A-CHAR-BATTLE-STATIC3D-SHIJIAHOU-ASSET-02：唯一原包的Blender清理、十视图本人批准、冻结FBX及factory-empty验证；无从零几何造模。
4. 既有U-CHAR-BATTLE-STATIC3D-PROFILES-01：等待本清理卡和男主ASSET-04，消费两份合法输入后导入石甲兽v2、建立双Profile；后续正式接线/反馈/用户实机跑测沿用现有链。
- 三张生产卡各一个独立结果，逐卡验收提交；旧ASSET-01保留blocked失败现场，不再进入生产或作为新链前置。首张四图卡先ready，后两张blocked。用户未批准四图时退出ready等回复，不自动继续。
- 关闭重制链仅以三叶子及本人批准真实完成为准；模型文件存在、技术测试和AI审图不能替代用户认可。

## 明确路径与所有者
- 四图：assets/source/characters/combat-pieces/shijiahou-static-3d-v2/input/下source_reference.png、shijiahou_front.png、shijiahou_left.png、shijiahou_right.png、shijiahou_back.png、manifest.json、prompts.md。
- 原包：assets/source/characters/combat-pieces/shijiahou-static-3d-v2/raw/下shijiahou_static3d_v2_platform_raw.zip、shijiahou_static3d_v2_platform_manifest.json、shijiahou_static3d_v2_platform_contact_sheet.png。
- 清理：assets/source/characters/combat-pieces/shijiahou-static-3d-v2/下shijiahou_static3d_v2.blend/.fbx、shijiahou_static3d_v2_basecolor.png、shijiahou_static3d_v2_manifest.json、shijiahou_static3d_v2_contact-sheet.png；QA记录为开发管理/石甲兽v2静态3D清理QA记录.txt。
- 正式Unity稳定目录仍是src/Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/，只由Profile卡写入；新叶子不写Unity。
- source按项目规范留本地并核对主工作区同路径字节；Git记录合同、任务和验证证据。禁止把仅在worktree的ignored文件当已交付。

## 交易与批准
- 本次路线授权不等于未知额度授权。Tripo服务、实际四图入口、单次报价与余额现场核验后，用户批准石甲兽资产ID、四图哈希、服务、一次和最大credits；不可借用男主45 credits。
- 四图、原包、清理十视图三次视觉批准均由用户本人针对实际候选作出，记录候选哈希、时间与原始回执来源。AI只填写checkedBy/技术检查结论，不得作为humanApprover写approved。
- 没有本人回执的状态为待批准；拒绝立即阻断对应下游。免费或付费第二次生成、换平台、绑定动画均不在本次冻结边界内。

## 技术与品质双验收
- 视觉上先对照批准原画，看头/四足/尾/甲片结构、体量与材质；四视图必须同一造型同一姿态，单图精细但跨图改变结构不通过。
- 技术保留旧合同+Y/+Z、1倍缩放、四足Y=0、最高Y=0.78m及包络；冲突不得通过压扁造型、镜像或私有偏移掩盖。
- 可导入、哈希正确、拓扑/材质/坐标通过仅证明技术可用。最终关中整体效果仍由用户实际跑测，不以截图替代。
- 验证按卡面最小充分范围；没有Unity/BattleSim修改不运行无关测试。
