import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
const evidence=path.resolve('evidence');await fs.mkdir(evidence,{recursive:true});
const base=process.env.SHIPMENT_API_URL ?? 'http://localhost:5001';
const password=process.env.DEMO_DISPATCHER_PASSWORD;if(!password)throw new Error('A local demo password is required.');
const browser=await chromium.launch({headless:true});
const context=await browser.newContext({viewport:{width:1505,height:1045},recordVideo:{dir:evidence,size:{width:1505,height:1045}},acceptDownloads:true});
const page=await context.newPage();const problems=[];const expected=[];
let validationExpected=false;
page.on('pageerror',error=>problems.push(error.message));
page.on('console',message=>{
  if(!['error','warning'].includes(message.type()))return;
  if(validationExpected && message.location().url.includes('/status') && message.text().includes('422'))expected.push(message.text());
  else problems.push(message.text());
});
async function text(id,value){await page.waitForFunction(({id,value})=>document.getElementById(id)?.textContent===value,{id,value},{timeout:20000});}
async function login(target){await target.goto(base);assert.match(await target.title(),/Shipment Desk/);await target.locator('[name=password]').fill(password);await target.getByRole('button',{name:'Sign in',exact:true}).click();await target.locator('#workspace').waitFor({state:'visible'});}
try {
 await login(page);
 await text('on-time','50%');await text('overdue','1');await text('dwell','3 h');await text('holds','1');
 assert.equal(await page.locator('#rows tr').count(),3);
 await page.screenshot({path:path.join(evidence,'desktop-baseline.png'),fullPage:true});
 await page.getByRole('button',{name:'Gate arrival',exact:true}).click();await page.locator('#evidence-dialog').waitFor({state:'visible'});
 assert.match(await page.locator('#evidence-fields').textContent(),/synthetic-baseline/);
 await page.locator('#evidence-dialog .close-dialog').click();
 await page.locator('#clock').fill('2026-10-12T08:10:00Z');await page.getByRole('button',{name:'Apply clock',exact:true}).click();
 await text('on-time','100%');await text('holds','0');
 await page.locator('#clock').fill('2026-10-12T12:00:00');await page.getByRole('button',{name:'Apply clock',exact:true}).click();
 await page.locator('#notice.error').filter({hasText:'timezone'}).waitFor();
 await page.locator('#clock').fill('2026-10-12T12:00:00Z');await page.getByRole('button',{name:'Apply clock',exact:true}).click();await text('on-time','50%');
 await page.locator('#search').fill('Pending arrival');assert.equal(await page.locator('#rows tr').count(),1);
 await page.getByRole('button',{name:'Select Pending arrival',exact:true}).click();await text('detail-title','Pending arrival');
 await page.locator('#search').fill('');
 await page.getByRole('button',{name:'New shipment',exact:true}).click();
 await page.locator('#shipment-form [name=description]').fill('Browser synthetic shipment');
 await page.getByRole('button',{name:'Create shipment',exact:true}).click();await text('detail-title','Browser synthetic shipment');
 await page.getByRole('button',{name:'Update shipment status',exact:true}).click();
 await page.locator('#status-form [name=newStatus]').selectOption('4');validationExpected=true;
 const invalidResponse=page.waitForResponse(r=>r.url().endsWith('/status') && r.request().method()==='PUT');
 await page.getByRole('button',{name:'Save status',exact:true}).click();assert.equal((await invalidResponse).status(),422);
 await page.locator('#status-dialog .dialog-error').filter({hasText:'niet toegestaan'}).waitFor();
 await page.screenshot({path:path.join(evidence,'validation-recovery.png'),fullPage:true});
 validationExpected=false;
 await page.locator('#status-form [name=newStatus]').selectOption('1');await page.getByRole('button',{name:'Save status',exact:true}).click();await page.locator('#status-dialog').waitFor({state:'hidden'});
 await page.getByRole('button',{name:'Upload document',exact:true}).click();
 await page.locator('#document-file').setInputFiles('../src/WebAPI/wwwroot/demo-manifest.pdf');
 await page.getByRole('button',{name:'Save document',exact:true}).click();await page.locator('#documents').filter({hasText:'demo-manifest.pdf'}).waitFor();
 const downloadPromise=page.waitForEvent('download');await page.getByRole('button',{name:'Download',exact:true}).click();
 const download=await downloadPromise;await download.saveAs(path.join(evidence,'verified-manifest.pdf'));
 assert.equal((await fs.readFile(path.join(evidence,'verified-manifest.pdf'))).toString('ascii',0,5),'%PDF-');
 const labels=['Start guided scenario','Record arrival','Open hold','Release and depart','Review KPIs'];
 for(let i=0;i<labels.length;i++){
  await page.getByRole('button',{name:labels[i],exact:true}).click();
  if(i<4)await page.getByRole('button',{name:labels[i+1],exact:true}).waitFor();
  else await page.locator('#tour-progress').filter({hasText:'Scenario complete'}).waitFor();
  await page.waitForTimeout(2500);
  if(i===4){await page.locator("#operations").scrollIntoViewIfNeeded();await page.waitForTimeout(2000);}
 }
 await text('on-time','33.3%');await text('overdue','1');await text('dwell','2.5 h');await text('holds','1');
 await page.screenshot({path:path.join(evidence,'desktop-complete.png'),fullPage:true});
 const mobile=await browser.newContext({viewport:{width:390,height:844}});const phone=await mobile.newPage();await login(phone);
 assert.equal(await phone.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true,'Mobile page must not overflow horizontally');
 await phone.screenshot({path:path.join(evidence,'mobile.png'),fullPage:true});await mobile.close();
 await page.getByRole('button',{name:'Sign out',exact:true}).click();await page.locator('#login').waitFor({state:'visible'});
 const customerName='synthetic-customer-'+Date.now();
 const registration=await page.request.post(`${base}/api/auth/register`,{data:{userName:customerName,email:customerName+'@example.invalid',password}});
 assert.equal(registration.status(),201);
 await page.locator('[name=userName]').fill(customerName);await page.locator('[name=password]').fill(password);
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.locator('#workspace').waitFor({state:'visible'});
 assert.equal(await page.locator('#rows tr').count(),0);await text('detail-title','Select a shipment');
 assert.equal(await page.locator('#documents li').count(),0);assert.equal(await page.locator('#new-shipment').isVisible(),false);
 await page.getByRole('button',{name:'Sign out',exact:true}).click();
 assert.deepEqual(problems,[],'Unexpected browser errors or warnings');
 await fs.writeFile(path.join(evidence,'browser-proof.json'),JSON.stringify({passed:true,url:base,title:await page.title(),viewports:[[1505,1045],[390,844]],checks:['identity','nonblank','no-framework-overlay','console','baseline-metrics','source-evidence-inspection','clock-recalculation','invalid-clock-recovery','filter','create','invalid-status-recovery','upload','download','guided-scenario','mobile-overflow','logout','customer-switch-clears-prior-data'],expectedValidationErrors:expected.length,unexpectedErrors:problems,baseline:{onTime:'50%',overdue:1,meanDwellHours:3,openHolds:1},afterGuidedScenario:{onTime:'33.3%',overdue:1,meanDwellHours:2.5,openHolds:1}},null,2));
 console.log('PASS: desktop/mobile real API flow, error recovery, PDF download, KPIs and logout; no unexpected browser errors.');
} finally {
 const video=page.video();await context.close();if(video)await fs.copyFile(await video.path(),path.join(evidence,'shipment-desk-walkthrough.webm'));await browser.close();
}
