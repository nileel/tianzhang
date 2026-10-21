// Export the editable SVG kit to PNGs and a Unity-ready atlas. No Unity files are written.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const sharp = require('sharp');
const dir = path.dirname(fileURLToPath(import.meta.url));
for (const sub of ['vectors', 'sprites', 'portraits', 'evidence']) fs.mkdirSync(path.join(dir, sub), { recursive: true });
const write = (name, text) => fs.writeFileSync(path.join(dir, name), text + '\n');
const colors = { panel:'#102D38', normal:'#193B46', hover:'#28515A', pressed:'#0B222B', selected:'#244D4C', disabled:'#24343B', cooldown:'#162F3C', ink:'#EFE9D3', secondary:'#C2D0CB', muted:'#A4B5B3', gold:'#D8BE80', line:'#768D87', health:'#8FBE75', mana:'#68C5D9', ap:'#E6BF62', enemy:'#D99982' };
const icons = {
  attack: ['普攻（武器）', 'M10 50 44 16l10-6-6 10-34 34 M10 40l14 14 M8 52l4 4 M21 43l-4-4'],
  guanshiji: ['贯石击', 'M8 42 20 16l10 7 13-8 11 18-8 17H15 M29 23l-5 12 12-5-4 18 M5 31h13 M7 26l5 5-5 5'],
  julingshu: ['聚灵术', 'M47 22c-3-9-24-11-29-2-6 10 25 13 30 3 M12 24C1 42 46 49 53 31 M15 40c8 14 29 12 35 2 M31 16c9-5 17 0 13 4'],
  feixingzhi: ['飞星指', 'M37 8l4 15 14 4-14 4-4 15-4-15-14-4 14-4z M10 11l2 6 6 2-6 2-2 6-2-6-5-2 5-2z M17 38l2 7 7 2-7 2-2 7-2-7-7-2 7-2 M26 15l-6-5'],
  move: ['移动', 'M23 10h20v8H23z M25 18l-3 17-12 9c-5 4-3 10 3 10h23l10-7-5-9-2-20 M24 27h15 M13 46h13l11-6'],
  defend: ['防御', 'M32 6 53 15l-4 23-17 19-17-19-4-23z M32 16v29 M23 26l9 19 9-19'],
  switch_art: ['换术', 'M19 10h32c5 0 5 8 0 8H20 M20 14v32c0 4-6 4-6 0V16c0-8-10-8-10-2 0 4 4 5 10 4 M14 50h31V18 M45 46c10-4 14 6 5 8H20 M28 27h10m-10 8h10'],
  equipment: ['换装', 'M22 9 12 15 5 28l10 6 6-8-3 29h28l-3-29 6 8 10-6-7-13-10-6c-1 12-19 12-20 0z M23 13l9 12 9-12 M32 25v22'],
  medicine: ['丹药', 'M26 7h12v8H26z M27 15v8c-17 8-20 30 5 32 25-2 22-24 5-32v-8 M21 34c7 5 15 5 22 0 M26 11h12'],
  divine: ['条件神通', 'M32 5l10 17 17 10-17 10-10 17-10-17L5 32l17-10z M32 19l13 13-13 13-13-13z'],
  unarmed: ['徒手普攻', 'M16 44l-4-16 8-4 6 8V14l8-2 2 16 6-10 8 4-2 22-12 12H22z'],
  close: ['关闭', 'M16 16l32 32 M48 16 16 48'],
  end_turn: ['结束行动', 'M8 32h43 M38 18l14 14-14 14'],
  cancel: ['取消', 'M22 16 8 30l14 14 M8 30h30c17 0 19 23 1 23'],
  log: ['战报', 'M16 8h33v48H16z M23 20h18m-18 10h18m-18 10h12 M10 16h6m-6 14h6m-6 14h6']
};
const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" width="120" height="120" viewBox="0 0 64 64">${body}</svg>`;
const assets=[];
function add(name, body, border=[0,0,0,0], kind='simple', label='') { assets.push({name,svg:svg(body),border,kind,label}); }
for(const [name,[label,d]] of Object.entries(icons)) add(`icon_${name}`,`<path d="${d}" fill="none" stroke="${colors.ink}" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/>`,undefined,'icon',label);
function panel(fill,line,emphasis=false) {
  return `<rect x="1" y="1" width="62" height="62" fill="${fill}" stroke="${line}" stroke-width="${emphasis?1.3:.7}"/><path d="M2 2h60" stroke="${line}" stroke-width="1.2"/><path d="M4 11V4h7 M53 4h7v7 M4 53v7h7 M53 60h7v-7" fill="none" stroke="${line}" stroke-width=".65"/>`;
}
for(const state of ['normal','hover','pressed','selected','disabled','cooldown']) {
  const line={normal:colors.line,hover:'#B4C8BC',pressed:'#B49E70',selected:colors.gold,disabled:'#64767A',cooldown:'#8AA9B7'}[state];
  add(`button_${state}`,panel(colors[state],line,state==='selected'),[24,24,24,24],'sliced',state);
}
add('panel_dark',panel(colors.panel,colors.line),[24,24,24,24],'sliced');
add('button_end',panel('#2D5C50',colors.gold),[24,24,24,24],'sliced');
add('frame_normal',panel('none',colors.line),[24,24,24,24],'sliced');
add('frame_selected',panel('none',colors.gold,true)+'<path d="M5 13V5h8 M51 5h8v8" stroke="#F8DE9B" fill="none" stroke-width="2"/>',[24,24,24,24],'sliced');
add('bar_track','<rect x="1" y="1" width="62" height="62" fill="#0B2027" stroke="#536E72"/>',[4,4,4,4],'sliced');
for(const key of ['health','mana','ap']) add(`fill_${key}`,`<rect width="64" height="64" fill="${colors[key]}"/>`,undefined,'fill');
add('cooldown_shade','<rect width="64" height="64" fill="#091A23" fill-opacity=".68"/>',undefined,'radial-fill');
add('timeline_current','<path d="M9 4v56h5V4z M20 26l14 6-14 6z" fill="#68C5D9"/>');
add('divider','<path d="M2 32h60" stroke="#768D87" stroke-width="1"/>',[4,0,4,0],'sliced');
if(assets.length!==32) throw new Error('Atlas capacity changed: '+assets.length);
const atlasPixels=Buffer.alloc(1024*512*4); const slices=[];
for(let i=0;i<assets.length;i++) {
  const a=assets[i]; write(`vectors/${a.name}.svg`,a.svg);
  const png=await sharp(Buffer.from(a.svg)).png().toBuffer(); fs.writeFileSync(path.join(dir,'sprites',a.name+'.png'),png);
  const x=(i%8)*128+4,y=Math.floor(i/8)*128+4;
  const rgba=await sharp(png).ensureAlpha().raw().toBuffer();
  for(let row=0;row<120;row++)rgba.copy(atlasPixels,((y+row)*1024+x)*4,row*120*4,(row+1)*120*4);
  slices.push({name:a.name,file:`sprites/${a.name}.png`,source:`vectors/${a.name}.svg`,kind:a.kind,label:a.label,rectTopLeft:[x,y,120,120],rectUnityBottomLeft:[x,512-y-120,120,120],borderLTRB:a.border,pivot:[.5,.5]});
}
await sharp(atlasPixels,{raw:{width:1024,height:512,channels:4}}).png().toFile(path.join(dir,'compact-ui-atlas.png'));
const layout={reference:[1920,1080],scale:'uniform Fit; 1366x768 = 0.711111, centered',rectOrigin:'top-left; x,y,w,h',resource:[32,24,416,188],timeline:[32,232,104,512],location:[740,24,440,64],withdraw:[1696,24,192,60],bounty:[1536,112,352,112],details:[1536,232,352,376],preview:[1536,632,352,236],actions:[160,892,1728,164],actionViewport:[256,904,1412,140],endTurn:[1700,908,168,64],battleLog:[1700,992,168,48]};
const manifest={version:1,date:'2026-10-21',scope:'V4 art handoff only; no Unity integration',atlas:{file:'compact-ui-atlas.png',size:[1024,512],transparentGutter:4},import:{textureType:'Sprite (2D and UI)',spriteMode:'Multiple',meshType:'FullRect',pixelsPerUnit:100,sRGB:true,alphaIsTransparency:true,mipmaps:false,filter:'Bilinear',wrap:'Clamp',compression:'None',maxSize:2048},colors,layout,slices,portraits:[
 {unit:'player',file:'../../../../src/Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png',reuse:true},
 {unit:'enemy_shijiahou',profile:'combat_enemy_shijiahou_v1',file:'portraits/enemy_shijiahou.png',identity:'warm brown; left-facing; closed mouth'},
 {unit:'enemy_lieshijia',profile:'combat_enemy_lieshijia_v1',file:'portraits/enemy_lieshijia.png',identity:'cold blue; right-facing; open mouth'}
],font:'Assets/Art/UI/Guanzhong/Fonts/GuanzhongChinese.otf',fontSizesReference:{place:34,title:28,body:24,secondary:22,button:24,cost:22,reason:22},spacingReference:{outer:32,panel:20,gap:16,icon:40,minHit:64},actionMapping:{basic:'icon_attack; icon_unarmed for unarmed',spell_guanshiji:'icon_guanshiji',spell_julingshu:'icon_julingshu',spell_feixingzhi:'icon_feixingzhi',move:'icon_move',defend:'icon_defend',switchArt:'icon_switch_art',equip:'icon_equipment',medicine:'icon_medicine',conditionalDivine:'icon_divine'},statePriority:['disabled/cooldown','pressed','selected','hover','normal'],textPolicy:'No baked text, prices, shortcut numbers or counts. Dynamic names support two lines; full name/reason in preview. Quote values remain authoritative runtime data.'};
write('manifest.json',JSON.stringify(manifest,null,2));
console.log(`Exported ${slices.length} editable SVGs + PNG slices, atlas and manifest.`);
