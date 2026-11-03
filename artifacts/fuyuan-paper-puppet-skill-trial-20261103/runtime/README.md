# 苻渊纸偶实验：Unity 实际播放

2026-11-03。使用主任务交付的同一份 `fuyuan-rigid-puppet.fbx`，没有修改网格、骨骼或动作。正式《天章》工程未改。

- [三动作短预览](videos/three-actions-preview.mp4)：正斜方向 3，走路、施法、攻击各重复两次，9.93 秒。
- [完整六向视频](videos/six-directions.mp4)：三动作 × 六方向，29.8 秒。
- [交互播放器](Player/PaperPuppet.exe)：运行后可切换动作、六向及正／侧机位，支持暂停和重播。需保留整个 `Player` 文件夹。
- [关键帧](keyframes/)：[行走膝部穿袍](keyframes/walk-d3-f010.png)、[施法](keyframes/cast-d3-f030.png)、[攻击](keyframes/attack-d3-f023.png)。

## 实测结果

Unity 6000.3.18f1，Windows x64 独立播放器。三条动画为 `FuYuan_Puppet_Rig|walk`（1.333 秒）、`cast`（2 秒）、`attack`（1.5 秒）。实际导入 30 个蒙皮分件、115,904 顶点、144,432 三角面，两个 Standard 材质；外观材质的底色贴图已提取自该 FBX 并实际按 4096×4096 导入，内面使用源文件的深色材质。

实际屏幕抓取 1120×800、30 fps，共 894 帧，18 个动作／方向段，已写入 `capture/completed.txt`。两份 H.264 视频均完整解码通过。预览仅选择并重复实际帧，保留原界面与 Unity trial version 水印。

`hips` 在运行时确实有行走起伏：local Y 为 0.9600000–0.9640000 米，峰谷差 4 毫米；模型外层根坐标恒为零，`applyRootMotion=false` 没有吞掉这段腰部运动。施法与攻击的 hips local Y 均为 0.964 米。逐帧记录见 `capture/hips-runtime-samples.json`。

这是一份可以评估动作与结构的实验样本，不表示美术和穿插问题通过：肩肘开缝明显，行走时膝部黑面会穿出袍片，袖旁有小碎点。自动捕获只覆盖斜前机位的六朝向。包围盒 min Y=-0.1031 米是转换局部 AABB 后的保守下界，未被当作实际顶点或脚底穿地结论。

## 本次最小工程改动

`BuildPaperPuppet.cs` 增加实际导入统计，并将 FBX 内嵌 JPEG 提取后挂到原 `tripo_mat_aa5cdbc9` 材质；保留原着色参数。`PaperPuppetPlayer.cs` 只增加 hips 位置观测，不修改动画采样、机位、灯光或屏幕捕获逻辑。源码与导入设置保留在 `project-source/`。

第一次以隐藏窗口运行时屏幕捕获返回空图像；停止该进程后，同一构建在正常交互窗口完成全部抓帧。没有增加 RenderTexture 替代采集流程。失败日志保留在本机 `capture-hidden-screen-failed.log`。

## 证据与提交范围

- `runtime-validation.json`：任务范围、来源、实测与已知缺陷。
- `import-report.json`、`video-validation.json`：导入和解码统计。
- `capture/capture.json`、`capture/completed.txt`、`capture/hips-runtime-samples.json`：实际播放器证据。
- `resource-hashes.json`：输入、最终视频、源码、关键帧和完整播放器文件哈希。
- `compact-delivery-files.txt`：建议主任务定向暂存的精简文件；`Player/`、全部 PNG 序列和详细构建日志留在本机，不应整目录暂存。

独立工程位于 `D:/Temp/TianZhang-Unity/FuyuanPaperPuppet20261103`。构建入口为 `BuildPaperPuppet.Build`；启动实际播放器时使用正常可见窗口，参数 `-screen-width 1120 -screen-height 800 -capture <新目录> -logFile <日志>`。批处理构建不得与打开同一工程的 Editor 并行。
