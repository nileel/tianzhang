// Render and inspect the design prototype. This never opens or drives Unity.
const {chromium}=require('playwright');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const {pathToFileURL}=require('node:url');
(async()=>{
 const sandbox={};vm.createContext(sandbox);vm.runInContext(fs.readFileSync(path.join(__dirname,'states.js'),'utf8')+';this.items=sequences;',sandbox);
 const chosen=process.argv[2];
 const data=sandbox.items.filter(s=>!chosen||s.id===chosen);
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const page=await browser.newPage();const errors=[],results=[],interactions=[];
 page.on('pageerror',e=>errors.push(String(e)));page.on('requestfailed',r=>errors.push(r.url()));
 const url=pathToFileURL(path.join(__dirname,'index.html')).href;
 for(const [width,height] of [[1920,1080],[1366,768]]){
  await page.setViewportSize({width,height});
  fs.mkdirSync(path.join(__dirname,'visuals',String(width)),{recursive:true});
  for(const s of data)for(let f=0;f<s.frames.length;f++){
   await page.goto(url+'?sequence='+s.id+'&frame='+f);
   await page.evaluate(async()=>{await document.fonts.ready;const im=new Image();im.src=getComputedStyle(document.querySelector('.world')).backgroundImage.slice(5,-2);await im.decode();await Promise.all([...document.images].map(i=>i.decode()));});
   const metrics=await page.evaluate(()=>{
    const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}};
    const selectors=['.identity','.statuses','.turns','.objective','.dock','.quote','.hint','.review','.proof','.conflict','.caption'];
    const boxes=selectors.flatMap(k=>[...document.querySelectorAll(k)].map(e=>({name:k,...rect(e)})));
    const outside=boxes.filter(r=>r.x<0||r.y<0||r.x+r.w>innerWidth+1||r.y+r.h>innerHeight+1);
    const clipped=[...document.querySelectorAll('button:not(.skill),.skill .label,.skill .sub,.cost,.balance,.status,.resource b')].filter(e=>e.clientWidth&&((e.scrollWidth>e.clientWidth+2)||(e.scrollHeight>e.clientHeight+2))).map(e=>e.textContent.trim());
    const skills=[...document.querySelectorAll('[data-skill]')].map(x=>x.dataset.skill);
    const protectedBoxes=sequences[group].id==='sight'?[]:[
     {name:'player',x:.412*innerWidth,y:.37*innerHeight,w:.046*innerWidth,h:.19*innerHeight},
     {name:'target',x:(sequences[group].frames[frame].art==='maintained'?.47:.536)*innerWidth,y:.29*innerHeight,w:.043*innerWidth,h:.18*innerHeight},
     {name:'support',x:.653*innerWidth,y:.37*innerHeight,w:.053*innerWidth,h:.14*innerHeight},
     {name:'fire-and-landing',x:.565*innerWidth,y:.535*innerHeight,w:.07*innerWidth,h:.1*innerHeight}
    ];
    const overlap=(a,b)=>a.x<b.x+b.w&&a.x+a.w>b.x&&a.y<b.y+b.h&&a.y+a.h>b.y;
    const occlusions=boxes.filter(b=>b.name!=='.conflict').flatMap(b=>protectedBoxes.filter(p=>overlap(b,p)).map(p=>b.name+':'+p.name));
    return {outside,clipped,occlusions,skills,hiddenTargetMarkers:document.querySelectorAll('.target-label,.targeting').length,boxes};
   });
   const name=s.id+'-'+s.frames[f].id;
   await page.screenshot({path:path.join(__dirname,'visuals',String(width),name+'.png')});
   results.push({width,height,state:name,...metrics});
  }
 }
 // Minimal behavioral review: switching actions keeps statuses; cancel keeps the same HUD.
 await page.goto(url+'?sequence=fire&frame=3');
 const oldStatus=await page.locator('.statuses').innerText();
 await page.locator('[data-skill="move"]').click();
 interactions.push({name:'switch-action-preserves-domain',pass:oldStatus===await page.locator('.statuses').innerText()});
 await page.keyboard.press('Escape');
 interactions.push({name:'cancel-keeps-common-hud',pass:await page.locator('[data-skill="fire"]').count()===1&&await page.locator('[data-skill="star"]').count()===1&&oldStatus===await page.locator('.statuses').innerText()});
 await page.locator('[data-detail="story"]').click();
 await page.screenshot({path:path.join(__dirname,'visuals','1366','fire-detail.png')});
 await page.goto(url+'?sequence=fire&frame=8');await page.locator('#confirm').click({force:true});
 interactions.push({name:'insufficient-ap-does-not-confirm',pass:(await page.locator('#frame').inputValue())==='8'});
 await page.goto(url+'?sequence=sight&frame=3');await page.locator('#clash-next').click();
 interactions.push({name:'conflict-returns-same-hud',pass:await page.locator('.conflict').count()===0&&await page.locator('[data-skill="sun"]').count()===1&&await page.locator('[data-skill="star"]').count()===1});
 await page.goto(url+'?sequence=sight&frame=2');await page.keyboard.press('Tab');
 interactions.push({name:'conflict-focus-contained',pass:await page.evaluate(()=>!!document.activeElement.closest('.conflict'))});
 for(const f of [1,2,3,5]){
  await page.goto(url+'?sequence=sight&frame='+f);
  interactions.push({name:'hidden-state-no-target-marker-'+f,pass:await page.locator('.target-label,.targeting').count()===0});
 }
 await browser.close();
 const failures=results.filter(r=>r.outside.length||r.clipped.length||r.occlusions.length);
 const report={kind:'V3 design prototype only; not Unity runtime evidence',date:'2026-11-01',screenCount:results.length,errors,failures,interactions,results};
 fs.writeFileSync(path.join(__dirname,'review-check'+(chosen?'-'+chosen:'')+'.json'),JSON.stringify(report,null,2)+'\n');
 console.log(JSON.stringify({screens:results.length,errors,failures:failures.map(({state,width,outside,clipped,occlusions})=>({state,width,outside,clipped,occlusions})),interactions},null,2));
 if(errors.length||failures.length||interactions.some(x=>!x.pass))process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1});
