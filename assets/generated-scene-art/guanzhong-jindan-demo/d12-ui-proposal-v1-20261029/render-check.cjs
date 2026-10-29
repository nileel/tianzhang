// Run with Node + Playwright available through NODE_PATH. Outputs design captures only.
const {chromium}=require('playwright');
const fs=require('node:fs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 const page=await browser.newPage();
 const errors=[];page.on('pageerror',e=>errors.push(String(e)));
 const states=['create','loadout','prepare','battle','target','ability','invalid','enemy','hidden','conflict','victory','defeat','withdraw','save'];
 const results=[];
 for(const [w,h] of [[1920,1080],[1366,768]]){
  await page.setViewportSize({width:w,height:h});
  const out=path.join(__dirname,'visuals',String(w));fs.mkdirSync(out,{recursive:true});
  for(const state of states){
   await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href+'?state='+state);
   await page.evaluate(()=>document.fonts.ready);
   await page.waitForFunction(()=>Array.from(document.images).every(i=>i.complete));
   const metrics=await page.evaluate(()=>{
    const visible=[...document.querySelectorAll('button,input,select,h1,h2,h3,.panel,.dock,.modal')].filter(e=>e.getClientRects().length);
    const outside=visible.filter(e=>{const r=e.getBoundingClientRect();return r.left < -1||r.right>innerWidth+1||r.top<0||r.bottom>innerHeight+1;}).map(e=>e.textContent.trim().slice(0,60));
    const clippedText=[...document.querySelectorAll('button,h1,h2,h3')].filter(e=>e.scrollWidth>e.clientWidth+2||e.scrollHeight>e.clientHeight+2).map(e=>e.textContent.trim());
    const context=document.querySelector('.context')?.getBoundingClientRect();
    const dock=document.querySelector('.dock')?.getBoundingClientRect();
    return {outside,clippedText,detailOverlapsDock:!!(context&&dock&&context.bottom>dock.top),pageOverflow:document.documentElement.scrollWidth>innerWidth,images:[...document.images].map(i=>({loaded:i.naturalWidth>0,src:i.getAttribute('src')}))};
   });
   await page.screenshot({path:path.join(out,state+'.png')});
   results.push({resolution:[w,h],state,...metrics});
  }
 }
 // Review path: create -> build choice -> challenge -> select -> cancel -> enemy -> withdraw.
 await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
 await page.locator('#name').fill('长名字修士·沈行舟');
 await page.locator('[data-action="next"]').click();
 await page.locator('[data-action="preset:3"]').click();
 await page.locator('[data-action="prepare"]').click();
 await page.locator('[data-action="challenge:3"]').click();
 await page.locator('[data-action="enter"]').click();
 await page.locator('[data-action="select:飞星指"]').click();
 await page.locator('[data-action="pick"]').click();
 await page.locator('[data-action="cancel"]').click();
 await page.locator('[data-action="end"]').click();
 await page.locator('[data-action="finishEnemy"]').click();
 await page.locator('[data-action="back"]').click();
 await page.locator('[data-action="prepare"]').click();
 const retained=await page.locator('#app').innerText();
 const interaction={nameRetained:retained.includes('长名字修士·沈行舟'),presetRetained:retained.includes('尺度与阳照'),returnedToPreparation:retained.includes('先识战场')};
 await page.selectOption('#review-state','conflict');
 await page.locator('[data-action="conflictStart"]').click();
 await page.locator('[data-action="conflictWindow"]').click();
 const conflictWindow=await page.locator('.modal').innerText();
 await page.screenshot({path:path.join(__dirname,'visuals','1366','conflict-window.png')});
 await page.locator('[data-action="conflictWin"]').click();
 interaction.conflictStoryboard=conflictWindow.includes('按 A')&&(await page.locator('#app').innerText()).includes('败方领域仍在');
 await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
 await page.locator('[data-action="normal"]').click();
 await page.locator('[data-action="next"]').click();
 await page.locator('[data-action="enter"]').click();
 await page.locator('[data-action="tab:jindan"]').click();
 interaction.normalHasNoJindan=(await page.locator('#app').innerText()).includes('当前普通角色没有金丹能力');
 await page.locator('[data-action="select:移动"]').click();
 await page.locator('[data-action="pick"]').click();
 interaction.movementRemainsMovement=(await page.locator('.quote').innerText()).includes('移动');
 await page.selectOption('#review-state','invalid');
 // Exercise a pointer click on an aria-disabled control; the app must reject it.
 await page.locator('[data-action="confirm"]').click({force:true});
 interaction.invalidDoesNotSubmit=(await page.locator('#app').innerText()).includes('未提交、未扣费');
 await page.selectOption('#review-state','create');
 await page.locator('[data-action="jindan"]').click();
 await page.selectOption('#review-state','battle');
 await page.locator('[data-action="tab:jindan"]').click();
 await page.selectOption('#review-state','create');
 await page.locator('[data-action="normal"]').click();
 await page.locator('[data-action="next"]').click();
 await page.locator('[data-action="enter"]').click();
 interaction.modeSwitchClearsJindan=await page.locator('[data-action="select:御风起飞"]').count()===0;
 await page.selectOption('#review-state','ability');
 interaction.normalAbilityUsesOrdinaryPreview=(await page.locator('.context').innerText()).includes('飞星指')&&!(await page.locator('.context').innerText()).includes('展开藏真');
 const report={kind:'browser design review; not Unity runtime evidence',errors,interaction,results};
 fs.writeFileSync(path.join(__dirname,'review-check.json'),JSON.stringify(report,null,2)+'\n');
 await browser.close();
 const bad=results.filter(r=>r.outside.length||r.clippedText.length||r.detailOverlapsDock||r.pageOverflow||r.images.some(i=>!i.loaded));
 console.log(JSON.stringify({screens:results.length,errors,interaction,layoutFailures:bad},null,2));
 if(errors.length||bad.length||Object.values(interaction).some(v=>!v))process.exitCode=1;
})().catch(e=>{console.error(e);process.exit(1);});
