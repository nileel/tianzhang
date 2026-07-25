# 数据管线说明 (v2 — 文本 ID 化)

## 设计理念

所有游戏数据以 CSV 表格为唯一数据源。**显示文本统一通过 Language.csv 管理**，其他 CSV 表中仅引用文本 ID。Excel 编辑 → 导出 CSV → Unity 菜单 `天章/导入全部配置` → 生成 .asset

## 文本 ID 系统

- `Language.csv`：唯一文本映射表，`id, zh_CN` 格式，后续可追加 `en_US` 等列
- 所有其他 CSV 的文本字段均填写 ID（如 `spell_xuanshuizhou`），导入时自动解析为中文
- ID 命名规则：`类型_拼音`，如 `gongfa_baoyuanshouyi`、`realm_lianqi`、`item_lingshi_low`

## CSV 文件结构

| 文件 | 内容 | 生成目标 |
|------|------|----------|
| `Language.csv` | 文本 ID → 中文映射 | （导入时内存解析） |
| `GongFa.csv` | 功法配置（ID化） | `Data/GongFa/GongFa_*.asset` |
| `Spells.csv` | 术法配置（ID化） | `Data/Spells/Spell_*.asset` |
| `Skills.csv` | 神通配置（ID化） | `Data/Skills/Skill_*.asset` |
| `Characters.csv` | 角色模板（ID化） | `Data/Characters/Char_*.asset` |
| `Enemies.csv` | 敌人模板（ID化） | `Data/Characters/Char_Enemy_*.asset` |
| `EnvironmentProfiles.csv` | 环境档案纯数据契约 | `Data/EnvironmentProfiles/EnvironmentProfile_*.asset` |

## CSV 格式规则

- 首行为字段名，后续每行为一条数据
- `#` 开头为注释，导入时跳过
- 字段分隔：`,` | 数组分隔：`|` | 键值分隔：`:`
- **文本字段填 ID**（不是中文），ID 在 Language.csv 中定义

## EnvironmentProfiles.csv 契约

本表定义环境档案结构；当前唯一生产行为 `env_guanzhong_wild`。表头固定为：

`profileId,directedEdges,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs`

- `directedEdges`：完整格式为 `unitsPerRange=<正整数>;maxQueryRange=<正整数>;edges=<边列表>`。边列表以 `|` 分隔，每项为 `fromQ:fromR>toQ:toR@metricDistanceUnits@allowsMovement@allowsEffects`；两个许可字段只接受 `0`/`1`。缺少查询上限、非拓扑邻格、非正边长、非法许可值或重复有向边均拒绝导入。
- `surfacePrototypeRefs`：以 `|` 分隔的地表原型 ID 引用。
- `phenomenonChannels`：以 `;` 分隔的六个通道声明，格式为 `channel=typeA+typeB`。通道必须恰为 `airflow`、`visibility`、`temperature`、`precipitation`、`suspendedHazard`、`cloudDischarge` 各一次。
- `phenomenonPairs`：以 `|` 分隔的同通道无序配对，格式为 `channel:typeA+typeB>resultType`。三个类型引用必须已在对应通道声明；翻转的同一对视为冲突并拒绝导入。
- `elementRelationRefs`：以 `|` 分隔，且必须恰含 `element_wood`、`element_fire`、`element_earth`、`element_metal`、`element_water` 各一次。

导入器会先验证整张表；任何缺字段、未知引用、非法通道、非相邻边、重复或顺序冲突配对都会在创建或更新 `.asset` 前失败。

## 添加多语言

1. 在 Language.csv 中追加列：`id, zh_CN, en_US, ja_JP`
2. 修改 DataConfigImporter 的 `T()` 方法，按当前语言选择对应列
3. 重新导入即可

## 工作流

1. 在对应 CSV 中编辑数据（文本字段用 ID）
2. 如需新增文本，先在 Language.csv 中添加 ID→中文映射
3. Unity 菜单 → `天章/导入全部配置`
4. 生成的 .asset 中显示为已解析的中文
