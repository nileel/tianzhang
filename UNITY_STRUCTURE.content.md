# Unity Content 与导入结构

## 何时读取

修改 CSV、ScriptableObject schema、内容目录、导入器、Unity 资产加载或正式场景内容绑定时读取。

## 主要路径

- CSV 与说明：`src/Assets/DataConfig/`
- 不可变 schema／目录：`src/Assets/Scripts/Modules/Content/`
- 领域导入器：`src/Assets/Scripts/Editor/*ContentImporter.cs`
- 导入编排：`src/Assets/Scripts/Editor/ContentImportCoordinator.cs`
- 生成资产：`src/Assets/Data/`
- 点购生产资产：`src/Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset`
- Unity 适配：`src/Assets/Scripts/Modules/Infrastructure/UnityContent/`

## 数据链所有者

`Language.csv` 提供显示文本；其他 CSV 使用稳定 ID。`ContentImportCoordinator.ImportAll` 只确定导入顺序，Character、Combat、Cultivation、World、Settlement 与 Adventure importer 各自拥有读取、领域校验、投影与提交；任一领域失败时不得跨领域半提交或静默默认。

`ContentCatalogData` 是正式只读目录，解析据点、敌人、物品、悬赏、册界静态目录、册界站点与 Adventure 地图。`EnvironmentProfileAsset` 和 `SpatialQueryBoardFactory` 是显式 UnityContent 适配；Player C# 不调用 `Resources.Load`。

## 正式场景绑定

- StartMenu：`ContentCatalogData` 与唯一点购配置 asset。
- Settlement：内容目录、据点、悬赏、册界站点与显示资源。
- Adventure：内容目录、`AdventureMapData`、`EnvironmentProfileAsset`、攻击档案、单位 Prefab 与 Tile/Sprite 引用。
- `src/Assets/Resources/` 中正式场景直接序列化使用的 UnitMarker、Adventure Tile 与点购资产属于保留扫描基线；路径在 Resources 下不表示运行时按字符串加载。

## 验证提示

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1`
- `ContentImporterArchitectureTests`、各领域 importer／catalog 测试
- `SceneArchitectureEditorTests.PointBuyBindingIsIdempotentAndUsesOnlyProductionAsset`
- 改 asset／场景前以 `.meta` GUID 扫描 `.unity`、`.prefab`、`.asset`、`.controller` 与 `.anim`。

## 禁止修改

- 不把领域字段校验塞回 Coordinator 或通用 CSV reader。
- 不引入 `Resources.Load`、静默 fallback、双写目录或第二内容所有者。
- 不手写 Unity 场景 YAML；场景变更走专用 Builder。

## 开放边界

渲染材质与 Shader 的 URP 兼容性由后续视觉预检读取本图的保留资产基线，本图不决定迁移策略。
