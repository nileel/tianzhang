# 苻渊关节人偶：完整主体四视图

2026-11-03。依据统一配色的结构图左栏，完整主体保留臂腿、短上衣、贴身前臂袖与长裤；不包含下垂袖片和长袍挂片。内置 image_gen 按正→背→左→右生成。

左右采用本人方向：left 鼻尖朝屏幕左，right 鼻尖朝屏幕右；上传槽位需逐图核对。四图均保留完整 T 姿势与躯干，不是已验证网格。背景实测透明，包围框及哈希见 image-qa.json；实际留白小于提示词目标。

## front

```text
Use case: stylized-concept. Generate ONE standalone full-body STRICT ORTHOGRAPHIC FRONT modeling reference for the COMPLETE CORE BODY of Fu Yuan, using ONLY the LEFT "完整主体" figure of the supplied construction sheet as the design source. The entire sheet is a reference, NOT the requested layout.

Keep the same lean elderly Chinese male face, white brows, pointed white beard, swept-back gray-white shoulder-blade-length hair, one modest topknot and narrow ribbons. Same dignified stylized game-puppet proportions, approximately six heads tall, not a bulky realistic soldier. Preserve charcoal ink-black matte crossed short tunic with sparse antique gold branching cracks, ivory inner crossed collar, dark tied waist sash, short tunic hem ending at upper thighs, dark shoes. BOTH complete fitted forearm coverings and BOTH complete trouser legs must use the SAME charcoal matte black and fine sparse muted antique-gold cracks as the accepted recolored left figure; not bare gray armor.

This model is a fully dressed articulated humanoid puppet with intact shoulder-upperarm-elbow-forearm-wrist-hand chains and intact hips-thighs-knees-shins-ankles-shoes. Slim fitted inner sleeves and trousers cover full limb volumes. The elbow and knee interfaces are readable with small aged-brass round pivot caps and neat overlapping dark joint segments. These are modeled joints, not holes, and no limbs are disconnected or missing. Small flat mounting tabs immediately below the elbows on the forearms and at the belt sides remain attached to the body. No long hanging sleeve flaps, no long waist robe panels, no long skirt, no hidden thighs: ALL those hanging accessories are separate generation units and must be absent here.

Pose: true symmetric T-pose; head upright facing camera, calm stern closed-mouth expression. Both arms straight and horizontal at shoulder height; elbows/wrists straight; palms facing down; five relaxed extended fingers naturally separated. Both legs straight, hip-width apart, knees and toes forward, visible background between entire trouser legs and shoes. Neutral assembly rest pose, no bent arm, no fight stance. Whole figure from topknot to soles, including entire fingertips, inside a square canvas with about 8% margin on every side. Soft neutral even lighting, clean readable local materials and limited ambient occlusion, sharp deliberate broad cloth planes, restrained painterly ink texture. No stone or metal armor gloss.

Output only ONE front view on a truly transparent background. No scene, base, shadow, diagrams, labels, red circles, grids, mounting arrows, measuring marks, extra body or detached objects. Preserve exact accepted body/outfit design; this is the left core figure reposed for modeling, not the fully assembled robe on the right.
```

## back

```text
Use case: identity-preserve. Create ONE strict orthographic BACK view of exactly the accepted complete articulated Fu Yuan core body in input 1. Rotate the same figure 180 degrees; do not redesign. Input 1 locks scale, body proportions, T pose, clothing, joint design, black/antique-gold crack palette and all complete limbs.
Same square canvas, same topknot and sole heights, whole silhouette inside frame. True T-pose with arms horizontal, straight elbows/wrists, palms down, legs straight and separated at hip width. The back of the hands faces up, side-facing thin hands at arm height; no added detached hand or new pose.
Gray-white hair: single topknot with narrow ribbons, long swept-back layered locks falling naturally across the upper back to shoulder blades, consistent with the front silhouette. The back of the head is hair only: NO face, eyebrows, beard or front collar repeated on rear head. Short charcoal-black matte tunic has restrained antique-gold branching cracks, ends at upper thighs, dark wrap belt encircling waist. No front sash bow or ivory crossed-front collar pasted onto back. Complete slim upper arms and forearms, full separate trouser thighs and shins and shoes, all in the same charcoal matte black and sparse gold-crack material. Small worn-brass joint caps and dark overlapping mechanical elbow/knee segments follow actual 3D joint construction. Rear belt mounting tabs may be visible and must remain small attached parts; do not invent a long coat or robe to hide the legs. No hanging sleeve accessories or long waist panels.
Keep neutral illumination, clear broad deliberate flat folds and consistent ink-painted finish, not polished armor. Genuinely transparent background, no text, labels, ground, shadows, pedestal or diagram. Exactly one full-body back view of the same core model, every arm and leg anatomically complete.
```

## left

```text
Use case: identity-preserve. Create ONE strict orthographic view of this SAME complete articulated Fu Yuan core body's OWN LEFT SIDE: the nose, chest and toes point SCREEN LEFT. Input 1 is the adopted front and input 2 the adopted back; together they lock the identical character, height, complete limb volumes, short tunic, joint layout, ink-black matte plus muted antique-gold crack palette and long white hairstyle.

Maintain the exact T-pose IN THREE DIMENSIONS: both upper arms and forearms stay horizontal at shoulder height, straight elbows and wrists, palms down. In this true 90-degree profile, the near left arm points TOWARD THE CAMERA, so the left hand is foreshortened and visible at shoulder height, the far arm hidden naturally. Do not lower the arm, bend it or invent a hand at waist. It is the same clothed arm, not a detached sleeve. Both full legs stay straight at their original hip-width separation along camera depth; one naturally overlaps the other in side view. Every hip/thigh/knee/shin/ankle/shoe volume exists beneath slim fitted black-and-gold trousers, no long skirt or hanging waist panels.
Preserve stern elderly face profile, long pointed divided beard in FRONT of chest, one topknot with narrow ribbon, coherent gray-white swept-back shoulder-blade hair behind the head, no face or beard on the rear. Charcoal short tunic with ivory front crossed collar, tied sash bow at FRONT only, hem at upper thighs. Same neat dark articulated joints with small brass pivot hardware, attached forearm/belt mount tabs. Exact same depth and thin cloth coverage as frontal/back references, not bulky armor. No hanging sleeve flap or long robe accessory.
Square canvas, same topknot/sole baseline and overall scale as front, entire silhouette visible, no crop. Soft neutral even illumination, painterly ink finish, broad clear intentional fold planes. Truly transparent background with no shadows, text, pedestal, measurement labels, collage or extra objects. ONLY one strict left profile, NOT three-quarter.
```

## right

```text
Use case: identity-preserve. Create ONE strict orthographic view of the same complete articulated Fu Yuan core body's OWN RIGHT SIDE: nose, chest and toes point SCREEN RIGHT. Input 1 adopted front locks identity and construction, input 2 adopted back locks hair and rear construction, input 3 adopted LEFT profile locks body depth and T-pose projection. Render the actual opposite side, not a mechanical mirror of the left image: crossed collar, sash knot/tails, topknot ribbons and mounting details follow the same 3D object.

Keep exact scale and full-height placement on the same square canvas, topknot and soles at matching heights. The entire silhouette remains inside the canvas. Head upright with stern elderly facial profile; gray-white long hair to shoulder blades and one topknot with narrow ribbons; pointed white beard only in FRONT of chest. Both complete arms remain horizontal at shoulder height in T-pose, palms down, elbows and wrists straight. The near right arm points toward the camera and its five-finger hand is naturally foreshortened at shoulder height. Do not drop any hand to the waist. Same separate complete hip-width legs overlapping naturally in profile, full trousers from hips to shoes. Slight front collar projection, slim side torso, short tunic ending at upper thighs, not an ankle-length robe.
Exact same charcoal ink-black matte and restrained antique-gold branching crack material throughout fitted forearm coverings and both trouser thighs/shins as adopted front. Small aged-brass joint details and forearm/belt mounting tabs remain physically attached. No new rivet pattern, no long sleeve flaps or hanging waist panels, no bulky armor. Consistent broad planar ink-painted finish, neutral soft illumination, no cast shadow. Truly transparent background. Only one strict right profile, no labels, no arrows, pedestal, extra objects or red marks.
```
