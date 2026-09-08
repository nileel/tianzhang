// Deterministic green-screen alpha extraction and rectangular part slicing only.
// Painting and structural corrections were performed by built-in ImageGen.
const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/WINDOWS/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const root = __dirname;
const regions = [
 ['torso',[60,25,275,292]], ['robe_back',[425,20,300,432]],
 ['robe_front',[800,25,210,425]], ['head',[1145,60,200,345]],
 ['hair_back',[85,490,200,320]], ['sleeve_near',[398,455,307,351]],
 ['sleeve_far',[750,455,298,351]], ['hand_near',[1150,485,255,288]],
 ['hand_far',[55,837,269,208]], ['foot_near',[425,848,225,186]],
 ['foot_far',[815,848,191,192]], ['cuff_lining',[1105,866,265,164]]
];
(async()=>{
 const {data,info}=await sharp(path.join(root,'parts-atlas-green-v2.png')).removeAlpha().raw().toBuffer({resolveWithObject:true});
 const rgba=Buffer.alloc(info.width*info.height*4);
 for(let i=0,j=0;i<data.length;i+=3,j+=4){
  const r=data[i],g=data[i+1],b=data[i+2],excess=g-Math.max(r,b);
  const a=255-Math.max(0,Math.min(255,(excess-20)*255/95));
  rgba[j]=r;rgba[j+1]=excess>20?Math.min(g,Math.max(r,b)):g;rgba[j+2]=b;rgba[j+3]=a;
 }
 fs.mkdirSync(path.join(root,'layers'),{recursive:true});
 const manifest=[];
 for(const [name,[x,y,w,h]] of regions){
  let minX=w,minY=h,maxX=-1,maxY=-1;
  for(let py=0;py<h;py++)for(let px=0;px<w;px++)if(rgba[((y+py)*info.width+x+px)*4+3]>8){minX=Math.min(minX,px);maxX=Math.max(maxX,px);minY=Math.min(minY,py);maxY=Math.max(maxY,py);}
  const left=Math.max(x,x+minX-6),top=Math.max(y,y+minY-6),width=Math.min(x+w,x+maxX+7)-left,height=Math.min(y+h,y+maxY+7)-top;
  await sharp(rgba,{raw:{width:info.width,height:info.height,channels:4}}).extract({left,top,width,height}).png().toFile(path.join(root,'layers',name+'.png'));
  manifest.push({name,sourceRect:[left,top,width,height],alphaThreshold:8});
 }
 fs.writeFileSync(path.join(root,'extraction.json'),JSON.stringify({source:'parts-atlas-green-v2.png',sourceSize:[info.width,info.height],operation:'chroma alpha + disjoint crop; torso ends immediately below belt',layers:manifest},null,2)+'\n');
 console.log(JSON.stringify(manifest));
})();
