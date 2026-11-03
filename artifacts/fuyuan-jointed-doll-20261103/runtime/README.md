# 苻渊关节人偶实验播放器

直接打开 [FuyuanJointedDoll.exe](Player/FuyuanJointedDoll.exe)，保留整个 `Player/` 目录。界面可切换走路、施法、攻击，暂停／重播，选择六个朝向及斜前／正／侧视。

- [三动作短片](three-actions-preview.mp4)：同一斜前视方向，各动作播放两遍，12.07 秒。
- [三动作六方向完整录制](three-actions-six-directions.mp4)：30fps，1120×800，共 1086 帧，36.2 秒。
- [Unity 导入报告](import-report.json)：7 个蒙皮网格，65205 个拆分后的渲染顶点，88955 个三角面；4 个 Standard 材质均为 4096×4096。
- [生成来源、费用和可编辑模型](../../../assets/source/characters/jointed-doll-20261103/README.md)：本轮 175 点，余额 2460 点。

录制来自实际 Unity Player 的 `ScreenCapture`，保留界面及 trial version 水印。原始帧、六向分段与髋骨实测在 `capture/`，播放器按 `-capture <绝对目录>` 可重录。`project-source/` 保存本次两个 C# 脚本、显式材质映射、包清单及 Unity 版本；完整临时工程在 `D:/Temp/TianZhang-Unity/FuyuanJointedDoll20261103`。

本版结构是完整着衣主体＋两片袖挂片＋四片腰挂袍片。肢体保留连续表面，挂件独立单骨刚性，不使用布料物理。侧面仍有较大的袍片开口，袖片偏薄护板感，整体是硬质人偶实验，尚未达到连续宽袖长袍的正式美术效果。动画为结构验证用，整手刚性、头发不独立摆动。

构建与捕获通过不等于所有姿势无穿插。最终检查包含正／侧／背静态、屈肘屈膝和三动作六向录制，并抽查六张关键运行帧；原始完整录制供进一步判断。早期空画面捕获已独立存于 `capture-unit-mismatch/`，最终文件均来自后续正确单位导出的 `capture/`。

原始源码、输入与精选证据可进 Git；`Player/` 二进制、完整帧序列及模型源件保留本地，不能把本地提交当作其备份。
