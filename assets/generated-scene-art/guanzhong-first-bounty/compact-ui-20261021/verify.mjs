// Focused asset QA and browser-rendered layout proof; this is not a Unity test.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url),sharp=require('sharp'),{chromium}=require('playwright');
const root=path.dirname(fileURLToPath(import.meta.url));
const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json')));
// Use --visual-only only when PNGs, SVGs and the manifest have not changed.
const visualOnly=process.argv.includes('--visual-only');
const report=visualOnly?JSON.parse(fs.readFileSync(path.join(root,'evidence/validation.json'))):{scope:'Asset package + browser layout only; not Unity runtime proof',assets:[],layouts:[],errors:[]};
if(visualOnly){report.layouts=[];report.errors=report.errors.filter(x=>!x.startsWith('layout-')&&!x.startsWith('sheet-'));}
if(!visualOnly){
const atlas=await sharp(path.join(root,manifest.atlas.file)).raw().toBuffer({resolveWithObject:true});
for(const s of manifest.slices){
 const [x,y,w,h]=s.rectTopLeft,[ux,uy,uw,uh]=s.rectUnityBottomLeft;
 if(x<0||y<0||x+w>atlas.info.width||y+h>atlas.info.height||ux!==x||uy!==512-y-h||uw!==w||uh!==h)report.errors.push('Rect mismatch '+s.name);
 const crop=await sharp(path.join(root,manifest.atlas.file)).extract({left:x,top:y,width:w,height:h}).raw().toBuffer();
 const sprite=await sharp(path.join(root,s.file)).raw().toBuffer();
 if(!crop.equals(sprite))report.errors.push('Atlas/sprite mismatch '+s.name);
 if(!fs.existsSync(path.join(root,s.source)))report.errors.push('Missing vector '+s.name);
 report.assets.push({name:s.name,rect:s.rectTopLeft,border:s.borderLTRB});
}
for(const p of manifest.portraits){
 const {data,info}=await sharp(path.join(root,p.file)).ensureAlpha().raw().toBuffer({resolveWithObject:true});
 let clear=0,partial=0,solid=0; for(let i=3;i<data.length;i+=4){if(data[i]===0)clear++;else if(data[i]===255)solid++;else partial++;}
 const corners=[3,(info.width-1)*4+3,(info.height-1)*info.width*4+3,(info.height*info.width-1)*4+3].map(i=>data[i]);
 // These are head-and-shoulder portraits: lower shoulders intentionally meet the frame.
 if(!clear||!solid||corners.slice(0,2).some(a=>a!==0))report.errors.push('Alpha/top corners '+p.unit);
 report.assets.push({name:p.unit,size:[info.width,info.height],alpha:{clear,partial,solid,corners}});
}
const lum=h=>{const c=h.match(/\w\w/g).map(x=>parseInt(x,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return c[0]*.2126+c[1]*.7152+c[2]*.0722};
report.contrast={};for(const name of ['ink','secondary','muted','gold','enemy']){const a=lum(manifest.colors[name].slice(1)),b=lum(manifest.colors.panel.slice(1));report.contrast[name]=(Math.max(a,b)+.05)/(Math.min(a,b)+.05);if(report.contrast[name]<4.5)report.errors.push('Low text contrast '+name);}
}
const browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 for(const [view,width,height] of [['sheet',1720,1140],['layout',1920,1080],['layout',1366,768]]){
  const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:1});
  page.on('pageerror',e=>report.errors.push(e.message));
  await page.goto(pathToFileURL(path.join(root,'index.html')).href+(view==='layout'?'?view=layout':''));
  await page.evaluate(async()=>{await document.fonts.ready;await Promise.all([...document.images].map(i=>i.decode().catch(()=>null)));});
  const check=await page.evaluate(()=>{
   const result={broken:[...document.images].filter(i=>!i.complete||!i.naturalWidth).map(i=>i.src),overflow:[],rects:{}};
   for(const el of document.querySelectorAll('.placed,.tile,.facts,.name,.skin')){if(!el.getClientRects().length)continue;if(el.scrollHeight>el.clientHeight+2||el.scrollWidth>el.clientWidth+2)result.overflow.push({element:el.id||el.className,text:el.textContent.trim(),client:[el.clientWidth,el.clientHeight],scroll:[el.scrollWidth,el.scrollHeight]});}
   for(const el of document.querySelectorAll('.placed')){const r=el.getBoundingClientRect();result.rects[el.id]=[r.x,r.y,r.width,r.height];}
   return result;
  });
  const screenshot=`evidence/${view}-${width}.png`;await page.screenshot({path:path.join(root,screenshot),fullPage:view==='sheet'});
  report.layouts.push({view,width,height,screenshot,...check});
  report.errors.push(...check.broken.map(x=>'Missing image '+x),...check.overflow.map(x=>`${view}-${width} overflow: ${JSON.stringify(x)}`));
  if(view==='layout'){
   const rs=Object.entries(check.rects);for(let i=0;i<rs.length;i++)for(let j=i+1;j<rs.length;j++){const [a,A]=rs[i],[b,B]=rs[j];if(Math.min(A[0]+A[2],B[0]+B[2])>Math.max(A[0],B[0])+1&&Math.min(A[1]+A[3],B[1]+B[3])>Math.max(A[1],B[1])+1)report.errors.push(`Panel overlap: ${a}/${b}`);}
   for(const [id,[x,y,w,h]] of rs)if(x<-.1||y<-.1||x+w>width+.1||y+h>height+.1)report.errors.push('Outside viewport '+id);
  }
  await page.close();
 }
}finally{await browser.close();}
fs.writeFileSync(path.join(root,'evidence/validation.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({assets:report.assets.length,layouts:report.layouts.length,contrast:report.contrast,errors:report.errors},null,2));
if(report.errors.length)process.exitCode=1;
