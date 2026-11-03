# 苻渊关节人偶结构示意

2026-11-03。用户要求先出示意图确认：主体生成完整手臂和双腿，红圈中的下垂袖片、长袍片单独生成，随后挂回主体。本阶段只交概念图，未提交 Tripo，未新增 Studio 消费。

- 用户设计依据：`user-marked-reference.png`（本轮上传的红圈参考）。
- 内置 image_gen 输出：`jointed-doll-assembly-concept.png`。
- 三栏依次为完整着衣主体、独立挂件、装配效果。
- 暂拟两片袖挂片、四片腰挂袍片。袖片根部属于前臂，腰片根部属于腰带；主体保留上臂、前臂、手、髋、大腿、小腿和鞋。头发与角色身份沿用用户参考。
- 结构上可将肢体生成和衣片生成分开，避免上一轮整袍外壳缺少内部臂腿的问题。挂件仍需独立的完整背面、厚度、接口与活动间隙；独立生成不保证自动装配或不穿插。
- 实际图像已核对：两只袖挂片、四片长袍挂片均画出；主体双臂双腿完整且分开。右栏保留宽袖长袍外形。图中铆点用于表达挂接关系，具体挂点数量、轴向与尺寸尚未定稿；此图不能作为已验收的工程装配图或直接代替建模四视图。
- 用户随后要求内层肢体与衣服统一配色，并说“那你开始做吧”。已生成 `jointed-doll-assembly-concept-v2-unified-color.png`，继续制作记录见 [独立四视图与生成](../fuyuan-jointed-doll-production-20261103/README.md)。

## 内层肢体配色修订

以下提示词以原示意图为输入，使用内置 image_gen 编辑；保存原版，修订版单独命名。前臂和裤腿采用炭黑旧金裂纹，保留关节及挂点。

```text
Use case: precise-object-edit. Edit the supplied Fu Yuan articulated-doll construction sheet with ONE narrowly scoped material/color correction. Preserve the entire three-column layout, all Chinese labels, exact character identity, face, hair, body proportions, poses, all joint and mounting shapes, two detached sleeve panels, four detached waist panels, accessories, garment silhouette, lighting and background. Change ONLY the plain gray forearm coverings and plain gray trousers (both thighs and both shins) on the left complete-body figure to match the upper tunic's charcoal INK-BLACK matte material and restrained ANTIQUE-GOLD branching crack pattern. Use somewhat thinner and sparser gold lines than the outer tunic, so these remain a quiet fitted inner layer. They should clearly belong to the same outfit, not bare gray wood or metal plates. Apply the SAME correction to any exposed corresponding forearm/trouser areas in the right assembled figure. Keep knees, elbows, wrists, ankles and small aged-brass pivot caps fully readable and unchanged; do not paint over brass or skin. Do not change the detachable clothing panels in the center. No added clothing shapes, no long skirt on the left body, no new accessories, no removal of complete arms or legs. Match the original image dimensions and crop exactly. This is only the approved inner-limb color and texture edit.
```

## 实际提示词

```text
Use case: stylized-concept.
Create a polished WIDE landscape character construction concept sheet for a Chinese fantasy game, using the attached image as the decisive identity, costume and art-style reference. The red hand-drawn circles are annotations identifying DETACHABLE GARMENT PARTS, not costume markings. Remove the red marks from the new sheet.

Core request: show a viable ARTICULATED DOLL with a fully modeled, fully clothed humanoid body and COMPLETE arms and legs. The two types of circled hanging garments will be generated as SEPARATE rigid accessories and attached afterward. This is a structural design proposal for the same Fu Yuan, not a finished mesh or a new character.

Preserve: stern lean elderly Chinese Daoist face, white brows and pointed white beard, gray-white long swept-back hair, one topknot and narrow tied ribbons, slim proportions, charcoal ink-black crossed robe with sparse antique-gold cracks, ivory collar/lining, dark cloth trousers and dark shoes. Match the supplied hand-painted miniature/paper-and-wood jointed puppet aesthetic, purposeful flat folds, restrained small aged-brass joint pins; no robots, heavy armor, gears or exaggerated metal mechanisms. No stone pedestal.

Design one coherent sheet with three clear columns, generous spacing and pale warm-gray paper background. The SAME character, same face, scale, costume and component shapes across all columns. Use detailed readable concept rendering, fine dark outlines and neutral illumination. Full figures and all part edges visible.

LEFT column, heading exactly “完整主体”:
A full-length near-frontal A-pose, arms about 35 degrees away from body, legs slightly apart. Show the fully intact articulated doll AFTER REMOVING ONLY the long hanging sleeve panels and long hanging waist robe panels. Upper body is dressed in its charcoal wrap tunic ending near the hips with the original ivory cross collar and tied waist sash. Both arms remain anatomically complete: shoulder, upper arm, visibly readable elbow joint, full forearm, wrist and five-finger hand; slim close-fitting dark inner sleeves cover the actual arm segments. Both legs remain fully present and visibly separate from hips to shoes: fitted dark trousers over full thighs, readable knee hinges, full shins, ankles and shoes. NO continuous ankle-length skirt on this base body. Do not remove any limbs to make room for the hanging parts. Joint construction is discreet warm-dark material with small brass circular pivots at elbows/knees, not exposed skin or skeletal robotics. Clearly indicate small forearm attachment tabs immediately below the elbow joint, and four belt/hip attachment points. Label these with two small readable callouts “前臂挂点” and “腰侧挂点”. Natural body proportions matching reference, not muscular, nude, faceless, chibi or a generic mannequin.

MIDDLE column, heading exactly “独立挂件”:
A tidy detached parts display at the SAME character-relative scale, no body hidden in any accessory.
Top: exactly TWO broad angled hanging sleeve flaps, left/right pair, matching the red-circled sleeve in the reference: thin crisp broad black-gold fold surfaces, ivory narrow lining edge, short top attachment tabs/pivot holes. They are hollow-backed or shallow curved CLOTHING PANELS, NOT complete sleeves containing arms, hands, cylinders or extra elbows. Each hangs from an attachment rooted on its respective FOREARM just below the elbow and moves with that forearm; it must not bridge the elbow or tie upper arm to forearm. Text exactly “袖挂片 ×2”.
Bottom: exactly FOUR long tapered black-gold robe panels, a front pair and a rear/side pair, all independently separable, with ivory narrow inner edge where the reference calls for it. The reference's circled long panel defines their style. Each has one small reinforced hanging tab at its top. No pants, knees, calves or feet baked into these panels. Show their complete lower hems. Text exactly “腰挂袍片 ×4”.
Show the INNER/back surface of one existing displayed sleeve flap and one existing robe panel by their turned orientation, not by adding extra duplicate parts. Restrained 2–3 clean guide arrows communicate forearm mount and waist mount, no tangled crossing lines. No invented extra accessories.

RIGHT column, heading exactly “装配效果”:
Same full figure in a gentle three-quarter view, same A-pose with arms a little away from body so construction is readable. All two sleeve flaps and four long robe panels have been mounted over the COMPLETE dressed body from the left column, restoring the reference's recognizable long-robed Daoist silhouette. Main arms remain intact under the hanging sleeve flaps; sleeve pieces hang beside/below the forearm, do NOT substitute for a limb. Waist panels overlap conservatively and hang OUTSIDE the complete trousers, with a small designed gap/clearance around knees and enough division for stepping; feet and a narrow glimpse of trouser legs visible through the front opening. Keep a readable elbow and wrist instead of hiding all joint structure. Black and ivory layered garment appearance should strongly resemble the attached original. No giant exposed ball sockets, no wide gaping shoulder holes, no knee protruding through a panel. Include a small caption exactly “完整肢体 + 外挂衣片”.

Overall title exactly “苻渊 · 关节人偶结构示意”.
This sheet is a conceptual assembly proposal: attractive and visually faithful but precise about what is body and what is detachable clothing. All parts in the middle must correspond to the assembled outfit on the right. Limit text to the specified short Chinese labels. No red circles, no watermark, no stone hex base, no floor shadows, no fabric physics trails, no weapons, no extra figures, no extra legs, no disembodied hands in parts tray.
```
