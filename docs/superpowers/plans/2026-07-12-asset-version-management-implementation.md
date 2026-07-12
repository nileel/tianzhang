# 资产版本管理实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变当前 GitHub 主仓工作流的前提下，建立本地原始素材、Git LFS 正式资源、资源登记和条件化自动校验的可执行边界。

**Architecture:** `assets/source/` 是被忽略的本地原始素材工作区；`src/Assets/Art/` 是未来正式运行时二进制资源的唯一根目录，指定扩展名通过根目录 `.gitattributes` 存入 Git LFS。PowerShell 检查器使用 Git 属性作为事实源，验证正式二进制必须进入 LFS、Unity 文本资产不得进入 LFS，并仅在资产相关改动时由自动控制器调用。

**Tech Stack:** Git, Git LFS 3.7.1, GitHub, PowerShell 7, Unity 6, 项目既有 `check-*.ps1` 验证脚本。

---

## 文件结构与任务映射

- `.gitignore`：排除本地 `assets/source/` 原始素材工作区。
- `.gitattributes`：仅为 `src/Assets/Art/` 的正式二进制资源声明 LFS 属性。
- `docs/资源管理/美术资源版本管理规范.md`：定义导出、登记、Git tag 和百度网盘备份流程。
- `docs/资源管理/美术资源登记册.md`：提供受版本控制的正式资源批次登记模板。
- `tools/check-asset-versioning.ps1`：对资产路径执行 LFS 边界和对象完整性检查。
- `tools/tests/check-asset-versioning-tests.ps1`：用临时 Git 仓库覆盖通过、遗漏 LFS、文本资产误入 LFS 三种情形。
- `开发管理/任务列表/场景与Unity任务.txt`：登记 TQ-072～TQ-074 与 AVM-04。
- `开发管理/当前任务队列.txt`：只更新已登记任务范围；不把 AVM 条目插入近期队列表。
- `开发管理/开发-下一步建议.txt`、`开发管理/状态与建议维护规则.txt`、`开发管理/自动工作流规则.txt`：声明条件化资产检查和 P0 不抢占规则。

### Task 1: 为资产检查器建立红灯测试

**Files:**
- Create: `tools/tests/check-asset-versioning-tests.ps1`
- Test: `tools/check-asset-versioning.ps1`

- [x] **Step 1: 创建临时 Git fixture 和断言帮助函数**

在 `tools/tests/check-asset-versioning-tests.ps1` 写入以下完整测试骨架。它不修改工作区；每次运行创建并删除 `%TEMP%` 下唯一目录。

```powershell
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checker = Join-Path $repoRoot 'tools/check-asset-versioning.ps1'
if (-not (Test-Path -LiteralPath $checker -PathType Leaf)) { throw "Missing checker: $checker" }

function Assert-ExitCode {
  param([int]$Expected, [int]$Actual, [string]$Label)
  if ($Actual -ne $Expected) { throw "$Label expected exit $Expected but received $Actual." }
}

function New-Fixture {
  $fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("tzg-asset-versioning-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $fixture | Out-Null
  & git -C $fixture init --quiet
  if ($LASTEXITCODE -ne 0) { throw 'git init failed.' }
  New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'src/Assets/Art') | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'src/Assets/Scenes') | Out-Null
  [System.IO.File]::WriteAllBytes((Join-Path $fixture 'src/Assets/Art/icon.png'), [byte[]](0,1,2,3))
  [System.IO.File]::WriteAllText((Join-Path $fixture 'src/Assets/Scenes/Test.unity'), "%YAML 1.1`n", [System.Text.UTF8Encoding]::new($false))
  return $fixture
}

function Set-Attributes {
  param([string]$Fixture, [string]$Content)
  [System.IO.File]::WriteAllText((Join-Path $Fixture '.gitattributes'), $Content, [System.Text.UTF8Encoding]::new($false))
}

$fixtures = [System.Collections.Generic.List[string]]::new()
try {
  # Individual cases are appended below.
}
finally {
  foreach ($fixture in $fixtures) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
```

- [x] **Step 2: 加入“正式二进制被 LFS 覆盖”通过用例，并运行测试确认红灯**

在 `try` 内追加以下用例；此时检查器尚不存在，因此命令应以“缺少 checker”失败。

```powershell
$fixture = New-Fixture
$fixtures.Add($fixture) | Out-Null
Set-Attributes $fixture "src/Assets/Art/**/*.png filter=lfs diff=lfs merge=lfs -text`n"
& powershell -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture
Assert-ExitCode 0 $LASTEXITCODE 'LFS-covered binary fixture'
```

运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/tests/check-asset-versioning-tests.ps1
```

预期：失败并包含 `Missing checker`，证明测试先于实现存在。

- [x] **Step 3: 加入两个失败关闭用例**

在通过用例后追加以下两段。第一段缺少 PNG 的 LFS 属性，第二段错误把 Unity 场景纳入 LFS；两者都必须返回退出码 `1`。

```powershell
$fixture = New-Fixture
$fixtures.Add($fixture) | Out-Null
Set-Attributes $fixture "src/Assets/Art/**/*.wav filter=lfs diff=lfs merge=lfs -text`n"
& powershell -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture 2>$null
Assert-ExitCode 1 $LASTEXITCODE 'untracked binary fixture'

$fixture = New-Fixture
$fixtures.Add($fixture) | Out-Null
Set-Attributes $fixture @"
src/Assets/Art/**/*.png filter=lfs diff=lfs merge=lfs -text
src/Assets/Scenes/**/*.unity filter=lfs diff=lfs merge=lfs -text
"@
& powershell -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture 2>$null
Assert-ExitCode 1 $LASTEXITCODE 'Unity text asset fixture'
```

- [x] **Step 4: 提交测试先行变更**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/tests/check-asset-versioning-tests.ps1'
git add -- tools/tests/check-asset-versioning-tests.ps1
git diff --cached --check
git commit -m "test: define asset versioning checks"
```

### Task 2: 实现 Git LFS 资产边界检查器

**Files:**
- Create: `tools/check-asset-versioning.ps1`
- Modify: `tools/tests/check-asset-versioning-tests.ps1`
- Test: `tools/tests/check-asset-versioning-tests.ps1`

- [x] **Step 1: 实现检查器参数、路径和属性读取**

创建 `tools/check-asset-versioning.ps1`，使用以下完整实现。不要扫描 `assets/source/`；它是明确的本地忽略目录。

```powershell
[CmdletBinding()]
param(
  [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "ProjectRoot does not exist: $root" }
if (-not (Test-Path -LiteralPath (Join-Path $root '.git') -PathType Container)) { throw "ProjectRoot is not a Git worktree: $root" }

$artRelative = 'src/Assets/Art'
$artPath = Join-Path $root $artRelative
$binaryExtensions = @('.psd','.psb','.png','.jpg','.jpeg','.tga','.exr','.hdr','.wav','.mp3','.ogg','.flac','.aiff','.fbx','.blend','.mp4','.mov','.webm','.ttf','.otf')
$unityTextExtensions = @('.meta','.unity','.prefab','.asset','.mat')
$errors = [System.Collections.Generic.List[string]]::new()

function Get-RelativeGitPath {
  param([string]$Path)
  return ([System.IO.Path]::GetRelativePath($root, $Path) -replace '\\','/')
}

function Get-FilterAttribute {
  param([string]$RelativePath)
  $output = @(& git -C $root check-attr filter -- $RelativePath)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { throw "git check-attr failed for $RelativePath" }
  $parts = $output[0] -split ': ', 3
  if ($parts.Count -ne 3) { throw "Unexpected git check-attr output for $RelativePath: $($output[0])" }
  return $parts[2]
}

if (-not (Test-Path -LiteralPath $artPath -PathType Container)) {
  'check-asset-versioning: OK (src/Assets/Art does not exist yet)'
  exit 0
}
if (-not (Test-Path -LiteralPath (Join-Path $root '.gitattributes') -PathType Leaf)) {
  throw 'Missing .gitattributes while src/Assets/Art exists.'
}

$artFiles = @(Get-ChildItem -LiteralPath $artPath -Recurse -File)
foreach ($file in $artFiles) {
  $relative = Get-RelativeGitPath $file.FullName
  $extension = $file.Extension.ToLowerInvariant()
  if ($binaryExtensions -contains $extension -and (Get-FilterAttribute $relative) -ne 'lfs') {
    $errors.Add("ERROR`tASSET_LFS_MISSING`t$relative`tBinary runtime asset is not tracked by Git LFS.") | Out-Null
  }
}

$assetsPath = Join-Path $root 'src/Assets'
if (Test-Path -LiteralPath $assetsPath -PathType Container) {
  foreach ($file in @(Get-ChildItem -LiteralPath $assetsPath -Recurse -File)) {
    $relative = Get-RelativeGitPath $file.FullName
    if ($unityTextExtensions -contains $file.Extension.ToLowerInvariant() -and (Get-FilterAttribute $relative) -eq 'lfs') {
      $errors.Add("ERROR`tUNITY_TEXT_IN_LFS`t$relative`tUnity text asset must remain in ordinary Git.") | Out-Null
    }
  }
}

if ($errors.Count -gt 0) {
  'check-asset-versioning: FAILED'
  $errors | Sort-Object
  exit 1
}

& git -C $root lfs fsck
if ($LASTEXITCODE -ne 0) { throw 'git lfs fsck failed.' }
'check-asset-versioning: OK'
```

- [x] **Step 2: 运行测试确认绿灯**

```powershell
powershell -ExecutionPolicy Bypass -File tools/tests/check-asset-versioning-tests.ps1
```

预期：退出码 `0`；通过 fixture 返回 `0`，遗漏 LFS 与 Unity 文本误入 LFS 两个 fixture 均被检查器拒绝。

- [x] **Step 3: 对当前工作树运行无资源目录基线检查**

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1
```

预期：退出码 `0`，输出 `src/Assets/Art does not exist yet`；不得创建空 `Art` 目录。

- [x] **Step 4: 提交检查器实现**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-asset-versioning.ps1|tools/tests/check-asset-versioning-tests.ps1'
git add -- tools/check-asset-versioning.ps1 tools/tests/check-asset-versioning-tests.ps1
git diff --cached --check
git commit -m "feat: validate LFS asset boundaries"
```

### Task 3: 写入 Git/LFS 配置和资源操作规范

**Files:**
- Create: `.gitattributes`
- Create: `docs/资源管理/美术资源版本管理规范.md`
- Create: `docs/资源管理/美术资源登记册.md`
- Modify: `.gitignore`
- Test: `tools/check-asset-versioning.ps1`

- [x] **Step 1: 新增 LFS 属性文件**

创建根目录 `.gitattributes`，内容必须精确限定在正式资源根目录：

```gitattributes
src/Assets/Art/**/*.psd filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.psb filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.png filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.jpg filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.jpeg filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.tga filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.exr filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.hdr filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.wav filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.mp3 filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.ogg filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.flac filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.aiff filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.fbx filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.blend filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.mp4 filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.mov filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.webm filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.ttf filter=lfs diff=lfs merge=lfs -text
src/Assets/Art/**/*.otf filter=lfs diff=lfs merge=lfs -text
```

- [x] **Step 2: 为本地原始素材增加忽略规则**

在 `.gitignore` 的 `# Project documentation (local only)` 小节前插入：

```gitignore
# Local source-art workspace; back up outside Git.
assets/source/
```

- [x] **Step 3: 创建资源规范与登记册模板**

在 `docs/资源管理/美术资源版本管理规范.md` 写明以下不可省略规则：

```markdown
# 美术资源版本管理规范

## 目录边界

- `assets/source/` 保存本地原始工程文件，已被 Git 忽略。
- `src/Assets/Art/` 保存 Unity 构建需要的正式导出资源。
- `.meta`、`.unity`、`.prefab`、`.asset`、`.mat` 必须保持普通 Git 文本追踪。

## 单批资源流程

1. 在 `assets/source/` 编辑原始文件。
2. 导出并导入 `src/Assets/Art/`。
3. 更新资源登记册。
4. 运行 `git check-attr filter -- src/Assets/Art/示例资源.png`、`powershell -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1` 和相关 Unity 验证。
5. 提交资源指针、`.meta` 和登记册；可运行里程碑创建 Git tag。

## 原始素材备份

百度网盘只保存 `assets/source/` 的异地备份，不替代 Git 或 Git LFS。每个可用素材批次或每周一次，以 `YYYY-MM-DD_当前GitTag或working` 命名打包；上传后核对 SHA-256 或文件大小。密码、令牌和网盘登录信息不得写入仓库。
```

在 `docs/资源管理/美术资源登记册.md` 创建下表和一条说明：

```markdown
# 美术资源登记册

每个正式资源批次在提交前追加一行；原始路径相对于 `assets/source/`，导出路径相对于仓库根目录。

| 批次 | 原始路径 | 导出路径 | 用途 | 导出日期 | 导出者 | Git 提交或 tag | 百度网盘备份 |
|------|----------|----------|------|----------|--------|----------------|--------------|
```

- [x] **Step 4: 初始化 LFS 并验证配置文件**

```powershell
git lfs install --local
git check-attr filter -- src/Assets/Art/example.png
git check-attr filter -- src/Assets/Scenes/AdventureScene.unity
powershell -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1
```

预期：PNG 输出 `filter: lfs`；场景文件输出 `filter: unspecified`；检查器在尚无 `Art` 目录时返回 `0`。

- [x] **Step 5: 提交配置和文档**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths '.gitattributes|.gitignore|docs/资源管理/美术资源版本管理规范.md|docs/资源管理/美术资源登记册.md'
git add -- .gitattributes .gitignore docs/资源管理/美术资源版本管理规范.md docs/资源管理/美术资源登记册.md
git diff --cached --check
git commit -m "docs: establish LFS asset workflow"
```

### Task 4: 登记 P1 任务并接入条件化自动工作流

**Files:**
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/当前任务队列.txt`
- Modify: `开发管理/开发-下一步建议.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Test: `tools/check-review-text.ps1`

- [x] **Step 1: 在 Unity backlog 登记资产治理任务**

在 `开发管理/任务列表/场景与Unity任务.txt` 的近期任务表中，在 TQ-070 后增加：

```markdown
| AVM-01 / TQ-072 | P1 | Codex | 待处理（不抢占 G2/G3 P0） | Git LFS 资产版本管理基线：属性规则、本地原始素材边界、规范与登记册 |
| AVM-02 / TQ-073 | P1 | Codex | 阻塞（TQ-072） | 正式资源入库检查与条件化自动工作流 |
| AVM-03 / TQ-074 | P1 | Codex | 阻塞（首批真实资源） | 首批真实资源从原始文件、LFS、干净工作区恢复至 Unity 构建的闭环验证 |
| AVM-04 | P2 | Codex | 按季度或阈值触发 | 复盘 LFS 容量、拉取耗时和百度网盘备份可靠性；仅凭实证决定是否评估 UVCS |
```

在“G1/P1 任务执行边界”后新增“资产版本管理任务边界”：TQ-072 必须不创建假资源或空 `Art` 目录；TQ-073 必须有通过与失败 fixture；TQ-074 必须使用真实资源且验证干净工作区恢复；AVM-04 不得因主观担忧迁移 SVN/UVCS。

- [x] **Step 2: 保持当前队列只含既有 P0/P1 切片**

在 `开发管理/当前任务队列.txt` 的“不进入 `1` 队列的当前事项”中，将范围说明从 `TQ-055～TQ-071` 更新为 `TQ-055～TQ-074`，并追加“AVM-01～AVM-03 已登记于场景与 Unity backlog；在 G2/G3 P0 任务无可领取项时，才按依赖补位，当前不复制进近期表。”

- [x] **Step 3: 记录建议和验证入口**

在 `开发管理/开发-下一步建议.txt` 的“当前开发判断”追加：资产版本管理采用 GitHub + Git LFS；`assets/source/` 是本地忽略目录并以百度网盘备份；TQ-072～TQ-074 是不抢占 P0 的后续工程治理切片。

在 `开发管理/状态与建议维护规则.txt` 的“检查脚本入口”追加：当变更涉及 `.gitattributes`、`src/Assets/Art/` 或 `docs/资源管理/美术资源登记册.md` 时，运行 `powershell -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1`；没有这些路径时不要运行该检查。

- [x] **Step 4: 添加自动控制器条件规则**

在 `开发管理/自动工作流规则.txt` 的每轮第 6 步验证说明后追加：若本轮 `expectedPaths` 包含 `.gitattributes`、`src/Assets/Art/` 或 `docs/资源管理/美术资源登记册.md`，在暂存前运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1
```

否则跳过该检查；无论是否运行都保留现有行尾空白、`git diff --cached --check` 与任务自身验证要求。

- [x] **Step 5: 验证管理文本与路由**

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理
rg -n 'AVM-01 / TQ-072|AVM-02 / TQ-073|AVM-03 / TQ-074|AVM-04' 开发管理/任务列表/场景与Unity任务.txt
rg -n 'TQ-055～TQ-074|AVM-01～AVM-03' 开发管理/当前任务队列.txt
rg -n 'check-asset-versioning.ps1' 开发管理/状态与建议维护规则.txt 开发管理/自动工作流规则.txt
git diff --check
```

预期：文本检查和差异检查返回 `0`；四条 AVM backlog 条目各出现一次；近期队列不新增 AVM 表格行。

- [x] **Step 6: 提交工作流与 backlog 路由**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/任务列表/场景与Unity任务.txt|开发管理/当前任务队列.txt|开发管理/开发-下一步建议.txt|开发管理/状态与建议维护规则.txt|开发管理/自动工作流规则.txt'
git add -- 开发管理/任务列表/场景与Unity任务.txt 开发管理/当前任务队列.txt 开发管理/开发-下一步建议.txt 开发管理/状态与建议维护规则.txt 开发管理/自动工作流规则.txt
git diff --cached --check
git commit -m "docs: schedule asset versioning workflow"
```

## 执行记录

- 2026-07-12：Task 1 与 Task 2 先完成红绿验证，再合并为 `d010d95`，以避免将依赖尚不存在检查器的测试单独留在不可运行提交中。
- 2026-07-12：Task 3 已由 `befc09a` 完成；Task 4 已由 `743b30d` 完成。

## 延后执行的真实资源闭环

TQ-074（AVM-03）只在第一批真实美术资源已经存在时，依据 `docs/资源管理/美术资源版本管理规范.md` 另写一份以真实文件路径、真实 tag 和相关 Unity 测试为准的实施计划。当前仓库没有正式美术资源，故本计划不得创建占位资源、空 `Art` 目录或虚构登记记录。

## 计划自检

- 规格中的目录边界、LFS 规则、登记、百度网盘备份、条件化自动检查和 AVM-01～AVM-04 均有对应任务。
- 唯一依赖真实素材的 TQ-074 已显式排除，当前计划不会在内容冻结或资源缺失时创建占位资产。
- 计划不包含未完成占位语句或未定义的检查命令；所有新增命令、文件和失败条件均在任务中给出。
