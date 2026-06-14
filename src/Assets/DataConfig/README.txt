# 数据管线说明

## 设计理念

所有游戏数据（功法、术法、神通、角色、敌人）以 **CSV 表格**为唯一数据源，通过 Unity Editor 工具一键导入生成 ScriptableObject 资产。项目代码中不硬编码任何游戏数值。

## 工作流

```
Excel 编辑 → 导出 CSV → 放入 Assets/DataConfig/ → Unity 菜单 天章/导入全部配置 → 生成 .asset 文件
```

## CSV 文件结构

| 文件 | 内容 | 生成目标 |
|------|------|----------|
| `GongFa.csv` | 功法配置（含成长表+篇章加成） | `Data/GongFa/GongFa_*.asset` |
| `Spells.csv` | 术法配置 | `Data/Spells/Spell_*.asset` |
| `Skills.csv` | 神通配置 | `Data/Skills/Skill_*.asset` |
| `Characters.csv` | 角色模板 | `Data/Characters/Char_*.asset` |
| `Enemies.csv` | 敌人/怪物模板 | `Data/Characters/Char_Enemy_*.asset` |

## CSV 格式规则

- 首行为字段名（标题行），后续每行为一条数据
- `#` 开头的行为注释，导入时自动跳过
- 字段分隔符：逗号 `,`
- 嵌套数据分隔符：管道 `|`（数组）、冒号 `:`（键值对）
- 包含逗号的字段用双引号 `"..."` 包裹

## 嵌套格式示例

### 功法成长表（GongFa.csv growth 字段）
```
练气:3,3,2,3,1,2,1,0.2,0.3|筑基:40,30,15,23,13,20,8,0.5,1.0
```
格式：`境界:HP,MP,肉攻,神攻,肉防,神防,反应,移力,神识|...`

### 篇章加成（GongFa.csv chapters 字段）
```
守一篇:练气:3:3:0:0:0:0:0:0:守一印记说明|抱元篇:筑基:3:3:...
```
格式：`篇章名:境界:魂盾率:命中率:格挡率:暴击率:暴伤:闪避:神攻加成:神防加成:特殊效果|...`

### 装备术法/神通（Characters.csv）
```
玄水咒|沧浪击|安神符
```

### 掉落表（Enemies.csv dropTable 字段）
```
石甲碎片:30|劣质灵石:50
```
格式：`物品名:掉落概率%|...`

## 在 Unity 中使用

1. 打开 Unity 编辑器
2. 菜单栏 → `天章` → `导入全部配置`（或逐个导入各分类）
3. 生成后的 .asset 文件在 `Assets/Data/` 各子文件夹中
4. 在场景中引用这些 ScriptableObject 资产即可

## 新增数据流程

1. 在对应 CSV 文件中追加新行
2. Unity 中执行 `天章/导入全部配置`
3. 新生成的 .asset 自动出现在 Data 文件夹
