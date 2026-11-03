# 一致四视图与部件图提示词

## 设计依据与图像角色

先显示并阅读原画。用简短文字固定脸型与年龄感、身材、发型与完整长度、衣领与袖口结构、主色和材质、饰品数量及所属左右。看不见的背面按已知结构保守补全，并标明这是推定。

每次生成明确各输入的作用。原画管设计，已采用正面管尺度和画法，局部参考只管明确指定的细节。局部修改只改变目标区域。历史实际提示词可能写错细节，不能自动升级为设计规范。

按本次三维生成单位制作四方向输入。一体生成使用全身正、背、左、右四张；采用 Charakuru 的头／体分开生成路线时，使用同一设计下的头含完整发型四张、身体四张。已有采用图直接复用，按需补齐。全身比例参考仅在合装需要时补做，避免无条件重复制作全身四图和部件八图。Charakuru 的全身三视图槽位用于装配比例叠加，不能代替四方向建模输入。

## 姿势、方向和尺度

| 文件方向 | 实际可见面 | 鼻、胸、脚尖方向 | 本人左侧挂件 |
|---|---|---|---|
| front | 正面 | 朝观察者 | 在屏幕右 |
| left | 本人左侧，90°侧面 | 朝屏幕左 | 在近侧，按遮挡显露 |
| right | 本人右侧，90°侧面 | 朝屏幕右 | 在远侧，按遮挡隐藏 |
| back | 背面 | 背向观察者 | 在屏幕左 |

上传到外部服务时按其当前相机示意核对槽位，不能只对英文 Left／Right 文件名。非对称细节决定左右，不能用水平翻转假造另一侧。

新制人形使用头直立、闭嘴中性表情、双臂肩高水平的 T 姿势，肘腕伸直。双腿仅小幅分开，正背面两鞋内缘之间能看到背景，膝和脚尖向前。严格侧面允许手、腿、鞋互相遮挡。衣摆遮住的部位按设计处理，不能为了显露腿而抬高衣摆。

本轮手部经验补充——新制面向本项目 VRM 默认动作的角色，把掌心向下、五指自然伸直略分开写进四图约束，再核对目标骨架和实际网格的掌向。现有用户确认的姿势或其它目标骨架以当次约定为准。

同一部位的四张图采用同画布、尺度、基线和余白。以头顶、眼线、下颌、肩、腕、腰、衣摆、鞋底作可见地标。头部与身体各自的裁切缩放可以不同，合装比例须回到全身原画。

## 全身单视图模板

方括号内容由当前角色事实替换。每次生成一张、一个方向，先采用正面，再将它加入其它方向的输入。

```text
Create one full-body orthographic [front / back / character's own left-side /
character's own right-side] reference of the same character.
Image 1 defines the identity, age, proportions, hairstyle, outfit construction,
colors and asymmetric ornaments. [Image 2 is the accepted front view and defines
the scale, pose, framing and finish for this view.]

Preserve [observed identity features, exact ornament counts and sidedness].
Use an upright head, relaxed open eyes, a neutral closed mouth and a true T-pose.
Keep both arms horizontal at shoulder height, elbows and wrists straight,
[palms facing down and five fingers naturally extended and slightly separated],
and the legs slightly separated at hip width with knees and toes facing forward.
For [left / right], show the character's own [left / right] surface, with the
nose, chest and toes pointing screen-[left / right]. Keep true 90-degree profile.
Preserve the pose in depth, allowing natural arm and leg overlap in projection.

Keep the entire silhouette, longest hair tips, fingertips and shoe soles inside
the canvas. Match the accepted front view's canvas, scale, centerline and margins.
Use clear local material rendering and neutral even illumination.
Output one complete view on a genuinely transparent background, with no labels,
measurement lines, props added for display, or painted checkerboard.
```

## 头部与身体的边界

| 部位 | 包含内容 | 接口与遮挡 |
|---|---|---|
| head | 脸、眼、眉、耳、胡须、头、全部前后侧发、头饰 | 头至下颌与后脑，长发留至完整末梢；移除身体后补全可见发面 |
| body | 可插入头内的颈部、衣领、衣服、手脚、鞋、衣装附件 | 颈部只出现一次；保留衣领、袖口、层叠衣摆的厚度和开口 |

帽、角、耳饰、腰饰按原设计所属归类，避免重复或漏件。身体图去掉头脸、胡须与全部头发，头部图去掉脖子、肩与衣领。头颈可以重叠插接，接口不必强行焊接。旧工程有独立 hair 输入时沿用其明确边界，不能静默改成新两件式。

在全身模板上追加对应段落，并将全身构图段改为当前部件的完整包围范围。

```text
HEAD WITH HAIR
Isolate the head, face, ears, eyebrows, eyes, original facial hair, complete
hairstyle and its original head accessories. Include every long hair tip even
where it previously covered the torso. Complete only the hair surfaces revealed
by removing the body. The body owns the neck. Keep this head upright and neutral,
without neck, shoulders, collar or a display stand.
Preserve the observed age and face shape.
```

```text
CLOTHED BODY
Isolate the clothed body from the neck to both shoe soles, including the hands,
collar and clothing accessories. Include a short natural neck insertion into
the head. Keep the original garment silhouette, cuffs, lining and fold structure.
Remove the head, face, beard and all head hair. Keep the true T-pose and the same
small leg separation in all directions. Render both hands as anatomically
consistent five-finger hands, with wrists passing through the intended cuffs.
```

有胡须的头部背面需写清胡须受后脑和后发遮挡，不能从后脑透出。保留原有后发连续结构，不能因视角变化增生第二套发型。

## 宽袖和长衣的侧面补充

苻渊实际提示词中的有效机制是约束投影中能看到什么。宽袖角色可追加如下段落，方向和附属物按当前角色修改。

```text
In this strict side view the horizontal arms extend along camera depth.
The near hand is foreshortened at shoulder height and the far hand is naturally
occluded. The fabric hangs below the arm toward the hip. Keep the hand attached
to the shoulder-height arm through its cuff, with no extra hand hanging at waist
height. Keep both feet at their original depth and stance; a single visible shoe
silhouette is acceptable in strict profile. [Describe the near-side ornament
and the far-side ornament hidden by the body.]
```

这段用于侧面。宽袖外层与内衬需保持同一个袖口、折角和层叠关系，衣领、腰带、挂件必须从四个方向对得上。

## 材质质量的局部提升

从 Charakuru 的 brushup 方法提炼“在原有形体内部提升色层与材质”的机制，画风按角色事实选择。苻渊的老年脸、灰白发和暗色道袍不能套成少女、粉肤或高光动漫脸。

```text
Finish the existing forms to the approved [visual style]. Preserve identity,
age, silhouette, construction, palette, patterns and ornament count.
Develop layered color and soft form shading inside the existing surfaces.
Keep matte cloth softly modeled, dark cloth readable, pale lining subtly
chromatic, and metal highlights narrow and controlled. Refine existing hair
locks with broad sheen following curvature. Improve [specific deficient area].
Preserve viewpoint, pose, part boundaries and all unaffected structures.
```

生成 3D 的参考优先中性均匀光和可辨结构，避免强环境染色、硬投影或镜面闪点被解释成纹理／几何。仅改画法时使用已采用图作编辑目标，不能顺带改脸型、加饰品、加发束或换姿势。

## 图像验收

- 检查实际 PNG 尺寸、alpha 和轮廓。提示词要求的余白百分比不等于输出已达到。轮廓地标可按统一 alpha 阈值测量，同时检查边缘半透明杂点。
- 以固定尺度并排核对四张图的肩、腕、腰、鞋底和长发末端，区分透视错乱与真实投影遮挡。
- 比较原画的饰品数量、左右、衣装接缝、两鞋和五指。发现系统性错误先修正共同基准。
- 保存实际发送的提示词及其输入图、方向、采用输出关联。局部错误从良好的采用图定向编辑，连续两次同类修改无改善时回查输入和结构约束，再决定下一步。
