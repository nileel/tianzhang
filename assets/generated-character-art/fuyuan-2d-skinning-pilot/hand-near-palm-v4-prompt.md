# v4 未采用的掌面尝试

内置 ImageGen 1次，等待55秒；输入为v3原近手、远手与真实待机截图。结果 `hand-near-palm-v4.png` 未采用，没有进入最终图层、场景或录屏。v3原输入可从Git `0869382f` 还原。

失败原因：助手最初把“手反了”扩解成掌背/手性问题，生成结果本身又与远手同手性。用户随后明确标注肩→肘→腕→指尖的下垂弧线，证明本轮要改的是组装方向。最终复用v3原手背图，仅将组装角度从65°改为40°，保持原腕点；头/裸颈和后发直接向右平移36源像素。以下保留误解时的提示词用于追溯，不能作为当前需求或资产用途。

```text
Use case: precise-object-edit.
Asset type: ONE corrected isolated hand sprite for an existing Unity cutout puppet.
Input 1 is the current WRONG screen-right hand. Input 2 is the other hand for painting style and relaxed open-palm gesture. Input 3 is the whole assembled character and fixes the intended screen-right sleeve location.
Generate ONLY the corrected hand and a short bare wrist overlap. No character, sleeve or other body parts.
It is the anatomical LEFT hand of the front-facing man, mounted on SCREEN RIGHT. Correct the reversed-looking hand: show its PALM, lightly cupped and open, five anatomically coherent digits, matching the relaxed empty gesture of the other hand. The wrist enters from UPPER LEFT; the fingers extend LOWER RIGHT. Thumb is on the INNER / SCREEN-LEFT side of this hand, toward the man's torso, not stuck on the outer right edge. Keep a readable connection from forearm to wrist to palm, no bent-back wrist, no double thumb, no dangling or distorted fingers. Do not simply reproduce the current hand-back.
Same elderly Chinese man's pale warm skin, subtle age lines, delicate hand-painted guofeng game-art shading as input 2. Similar hand size and proportions. Short complete wrist overlap at upper-left, NO cloth cuff/ring, NO jewelry, NO effects.
1024x1024 square canvas, hand centered comfortably with 80px margins, perfectly flat solid CHROMA GREEN #00FF00 everywhere outside the hand. No shadows, checkerboard, labels or text. Keep the hand diagonal from upper-left wrist to lower-right fingers.
```
