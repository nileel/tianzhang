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

## CSV 格式规则

- 首行为字段名，后续每行为一条数据
- `#` 开头为注释，导入时跳过
- 字段分隔：`,` | 数组分隔：`|` | 键值分隔：`:`
- **文本字段填 ID**（不是中文），ID 在 Language.csv 中定义

## 添加多语言

1. 在 Language.csv 中追加列：`id, zh_CN, en_US, ja_JP`
2. 修改 DataConfigImporter 的 `T()` 方法，按当前语言选择对应列
3. 重新导入即可

## 工作流

1. 在对应 CSV 中编辑数据（文本字段用 ID）
2. 如需新增文本，先在 Language.csv 中添加 ID→中文映射
3. Unity 菜单 → `天章/导入全部配置`
4. 生成的 .asset 中显示为已解析的中文
