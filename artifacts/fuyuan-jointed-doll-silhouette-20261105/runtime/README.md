苻渊长垂袖修订版，2026-11-05。先看[设计／上轮／本轮三栏对照](design-before-after.png)，再看[三动作双向近景短片](three-actions-preview.mp4)和[三动作六方向完整录像](three-actions-six-directions.mp4)。

三栏图使用设计图右栏，以及两版模型的施法第 0 帧、朝向 3。上轮重新在本次播放器中实录，与本轮采用相同相机位置、旋转、正交半高和画幅。相机注视世界坐标 (0, 0.9, 0)，正交半高固定为 1.17 米。设计与实机按头顶和鞋底等比缩放到约 760 像素高；两版实机使用完全相同的裁切和放大率。对应相机数据和来源哈希见[验证记录](validation.json)与[上轮重录记录](baseline/capture.json)。

低臂主帧中的近侧袖尖已明显低于指尖，浅色内衬沿袖口连续延伸到低尖角，正面也能看到双袖完整的内外层。[低臂近景](low-arm-reference.png) · [正面近景](front-reference.png) · [施法近景](cast-reference.png)。本轮的长垂袖轮廓更接近设计。设计袖外侧仍更宽、更舒展，本轮袖面显得收束，袖根褶皱较集中，举臂时仍保留硬质衣片感。

短片包含走路、施法和攻击，每种动作各显示斜前和反向两个角度，共 362 帧、12.07 秒。它从原始 1120×800 录制的 (252, 48) 位置统一裁切 616×616，再放大至 924×924，保留头脚和袖尖，并添加动作与角度标签。六向录像保留原画幅与播放器界面，30 fps，共 18 段、1086 帧、36.20 秒。实际运行帧抽查覆盖六个朝向、三个动作，另对双向短片全帧检查了人物轮廓与裁切边界。

保留整个 `Player/` 文件夹，运行 [FuyuanJointedDoll.exe](Player/FuyuanJointedDoll.exe) 即可切换动作、六向、正视及侧视，支持暂停和重播。临时工程位于 `D:/Temp/TianZhang-Unity/FuyuanJointedDollSilhouette20261105`，本次实际 C# 脚本、包清单和版本已保存在 [project-source](project-source/)。曾出现增量构建的场景数据损坏，采用干净构建后完成本次录制。

Unity 6000.3.18f1 导入 7 个蒙皮网格、80,185 个渲染顶点、99,241 个三角面，四份材质的基础色贴图实际导入尺寸均为 4096×4096。三条片段均为 30 fps，走路 1.567 秒、施法 2.367 秒、攻击 1.967 秒。袖筒使用 `tripo_mat_29b70357`，最终 FBX 的 SHA-256 为 `657179dfe7e0cd5d9c8559978b010bf3f8999217d3baf93bc87d64a5699d221f`。139 个播放器文件复制后逐一比对哈希。

[Unity 导入报告](import-report.json) · [材质对应](material-map.json) · [实录帧段](capture/capture.json) · [模型来源、尺寸和网格验证](../../../assets/source/characters/jointed-doll-silhouette-20261105/README.md)
