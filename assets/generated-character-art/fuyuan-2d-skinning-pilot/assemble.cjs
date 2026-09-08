const fs=require('fs'),path=require('path');
const sharp=require('C:/Users/WINDOWS/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const root=__dirname, ppu=768, width=1024,height=1280,origin=[512,1160];
const parts=[
 {name:'foot_far',h:.11,p:[-.095,.005],anchor:[.5,0],z:0,order:0},
 {name:'foot_near',h:.12,p:[.105,0],anchor:[.5,0],z:0,order:1},
 {name:'sleeve_far',h:.43,p:[-.11,1.01],anchor:[.85,.904],z:25,order:2},
 {name:'hand_far',h:.14,p:[-.245,.725],anchor:[.91,.88],z:-15,order:3},
 {name:'robe_back',h:.77,p:[.025,.065],anchor:[.5,0],z:0,order:4},
 {name:'robe_front',h:.75,p:[0,.085],anchor:[.5,0],z:0,order:5},
 {name:'hair_back',h:.29,p:[.015+36/ppu,1.28],anchor:[.5,1],z:0,order:6},
 // Keep head/neck pixels and height; shift the whole layer to the user's marked position.
 {name:'head',h:.30*859/787,p:[-.018+36/ppu,1.335-.30*859/787],anchor:[.46,0],z:0,order:7},
 {name:'torso',h:.38,p:[.035,.72],anchor:[.5,0],z:0,order:8},
 {name:'sleeve_near',h:.45,p:[.17,1.02],anchor:[43/296,307/335],z:-25,order:9},
 // Keep the original back-of-hand; turn fingers downward along the marked arm arc.
 {name:'hand_near',h:.14,p:[.35,.755],anchor:[.88,.86],z:40,order:10}
];
(async()=>{
 fs.mkdirSync(path.join(root,'assembled-layers'),{recursive:true});
 const composite=[];
 for(const part of parts){
  const src=path.join(root,'layers',part.name+'.png'),meta=await sharp(src).metadata();
  const sh=Math.round(part.h*ppu),sw=Math.round(sh*meta.width/meta.height);
  const resized=await sharp(src).resize(sw,sh).png().toBuffer();
  const rot=await sharp(resized).rotate(-part.z,{background:{r:0,g:0,b:0,alpha:0}}).png().toBuffer({resolveWithObject:true});
  const a=-part.z*Math.PI/180,dx=(part.anchor[0]-.5)*sw,dy=(.5-part.anchor[1])*sh;
  const rx=Math.cos(a)*dx-Math.sin(a)*dy,ry=Math.sin(a)*dx+Math.cos(a)*dy;
  const left=Math.round(origin[0]+part.p[0]*ppu-rot.info.width/2-rx),top=Math.round(origin[1]-part.p[1]*ppu-rot.info.height/2-ry);
  const file=path.join(root,'assembled-layers',part.name+'.png');
  await sharp({create:{width,height,channels:4,background:{r:0,g:0,b:0,alpha:0}}}).composite([{input:rot.data,left,top}]).png().toFile(file);
  part.canvasRect=[left,top,rot.info.width,rot.info.height];
  composite.push({input:file,left:0,top:0});
 }
 await sharp({create:{width,height,channels:4,background:{r:57,g:66,b:71,alpha:1}}}).composite(composite).png().toFile(path.join(root,'assembly-preview.png'));
 fs.writeFileSync(path.join(root,'assembly.json'),JSON.stringify({canvas:[width,height],ppu,originTopLeft:origin,direction:1,yaw:150,parts,unused:['cuff_lining: generated as collar, rejected; sleeve artwork already includes continuous lining']},null,2)+'\n');
 console.log('11 aligned RGBA layers and static assembly preview saved.');
})();
