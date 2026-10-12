import { api, authenticate, downloadDocument } from './api.js';
import { calculateMetrics } from './metrics.js';
import { tour, advanceTour, resetTour } from './tour.js';
const $ = id => document.getElementById(id);
let shipments = [], selectedId = null, clock = $('clock').value, detail = null;
const cleanDescription = value => value.replace(/^Synthetic package demo: /, '').replace(/^spare parts$/, 'Spare parts');
const names = {arrival_plan:'Planned arrival',gate_in:'Gate arrival',gate_out:'Gate departure',hold_opened:'Customs hold opened',hold_released:'Customs hold released'};
function notice(message, error=false) { $('notice').hidden=false; $('notice').textContent=message; $('notice').classList.toggle('error',error); }
function clearInspector() { selectedId=null;detail=null;$('detail-title').textContent='Select a shipment';$('detail-reference').textContent='';$('timeline').replaceChildren();$('documents').replaceChildren();$('document-file').value='';$('open-upload').disabled=true; }
function setRoles(roles) { const operator=roles.includes('Dispatcher')||roles.includes('Administrator');$('new-shipment').hidden=!operator;$('record-event').hidden=!operator;$('event-type').hidden=!operator;document.querySelector('.event-controls').hidden=!operator;$('tour-next').hidden=!operator;$('status-button').hidden=!(operator||roles.includes('Driver'));if(!operator)$('tour-progress').textContent='Use a dispatcher account to run the guided scenario.'; }
function signOut() { authenticate(null);$("notice").hidden=true;$("notice").textContent=""; resetTour(); shipments=[]; detail=null; selectedId=null; $('workspace').hidden=true; $('login').hidden=false; $('sign-out').hidden=true; $('login-form').elements.password.value='';clearInspector();$('rows').replaceChildren();['on-time','overdue','dwell','holds'].forEach(id=>$(id).textContent='—');$('metric-note').textContent='';$('list-note').textContent='';$('tour-next').textContent='Start guided scenario';$('tour-progress').textContent='';document.querySelectorAll('.steps li').forEach(li=>li.classList.remove('complete','active'));document.querySelectorAll('dialog').forEach(dialog=>{dialog.close();dialog.querySelector('form')?.reset();if(dialog.querySelector('.dialog-error'))dialog.querySelector('.dialog-error').textContent='';}); }
async function action(button, fn, dialog=null) {
  button.disabled=true;
  try { await fn(); } catch(error) {
    if (error.status===401) signOut();
    if(dialog) dialog.querySelector('.dialog-error').textContent=error.message; else notice(error.message,true);
  } finally { button.disabled=false; }
}
function renderMetrics() {
  const m=calculateMetrics(shipments,clock);
  $('on-time').textContent=m.punctuality.rate===null?'—':`${Math.round(m.punctuality.rate*1000)/10}%`;
  $('overdue').textContent=m.classification.overdue;
  $('dwell').textContent=m.dwell.meanHours===null?'—':`${Math.round(m.dwell.meanHours*100)/100} h`;
  $('holds').textContent=m.openHolds;
  $('metric-note').textContent=`${m.punctuality.evaluated} completed arrivals evaluated; ${m.punctuality.excluded} excluded. ${m.dwell.evaluated} completed visits; ${m.dwell.excluded} excluded. Tolerance: 5 minutes. ${m.classification.futureActualExcluded ? m.classification.futureActualExcluded+" future-dated arrival(s) excluded. " : ""}${m.warnings.join(" ")} KPIs cover loaded shipments; search filters the table only.`;
}
function renderRows() {
  const query=$('search').value.trim().toLowerCase(); $('rows').replaceChildren();
  const visible=shipments.filter(s=>`${s.trackingNumber} ${s.description} ${s.statusLabel}`.toLowerCase().includes(query));
  for(const row of visible) {
    const tr=document.createElement('tr');tr.classList.toggle('selected',row.id===selectedId);
    const tracking=document.createElement('td'), button=document.createElement('button');button.className='row-select';button.textContent=row.trackingNumber;button.setAttribute('aria-label',`Select ${cleanDescription(row.description)}`);
    button.addEventListener('click',()=>action(button,()=>select(row.id)));tracking.append(button);
    const description=document.createElement('td');description.textContent=cleanDescription(row.description);
    const status=document.createElement('td'), label=document.createElement('span');label.className=`status ${row.statusLabel.toLowerCase()}`;label.textContent=row.statusLabel.replace(/([a-z])([A-Z])/g,'$1 $2');status.append(label);
    tr.append(tracking,description,status);$('rows').append(tr);
  }
  $('list-note').textContent=visible.length?`${visible.length} shipments shown. The preview loads at most 100 shipments.`:'No shipments match this view.';
}
async function select(id) {
  selectedId=id;$('open-upload').disabled=false;detail=(await api(`/api/shipments/${id}`)).data;renderRows();
  const row=shipments.find(s=>s.id===id);$('detail-title').textContent=cleanDescription(detail.description);$('detail-reference').textContent=detail.trackingNumber;
  $('timeline').replaceChildren();
  for(const envelope of row?.events ?? []) {
    const li=document.createElement('li'), time=document.createElement('time'), title=document.createElement('button');
    time.dateTime=envelope.event.occurredAt;time.textContent=new Date(envelope.event.occurredAt).toISOString().slice(11,16);
    title.textContent=names[envelope.event.type] ?? envelope.event.type;title.className='event-label';title.title='View source evidence';
    title.addEventListener('click',()=>{
      $('evidence-fields').replaceChildren();
      const values=[['Event',title.textContent],['Event ID',envelope.referenceId],['Visit or hold reference',envelope.correlationId ?? 'None'],['Source',envelope.event.evidence.source],[envelope.event.type==='arrival_plan'?'Planned target time':'Occurrence time',envelope.event.occurredAt],['Source observation time',envelope.event.evidence.observedAt],['Detail',envelope.event.evidence.detail]];
      for(const [label,value] of values){const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=label;dd.textContent=value;$('evidence-fields').append(dt,dd);}
      openDialog('evidence-dialog');
    });
    li.append(time,title);$('timeline').append(li);
  }
  if(!row?.events.length){const li=document.createElement('li');li.textContent='No operational evidence recorded.';$('timeline').append(li);}
  $('documents').replaceChildren();
  for(const doc of detail.documents) {
    const li=document.createElement('li'), text=document.createElement('span'), download=document.createElement('button');text.textContent=doc.fileName;download.className='text-button';download.textContent='Download';
    download.addEventListener('click',()=>action(download,()=>downloadDocument(doc.id,doc.fileName)));li.append(text,download);$('documents').append(li);
  }
  if(!detail.documents.length){const li=document.createElement('li');li.textContent='No documents yet.';$('documents').append(li);}
}
async function refresh(id=selectedId) {
  const result=(await api('/api/operations')).data;shipments=result.shipments;
  renderMetrics();renderRows();if(id && shipments.some(s=>s.id===id)) await select(id);else if(shipments.length) await select(shipments[0].id);else clearInspector();
}
function openDialog(id) { const dialog=$(id);if(dialog.querySelector('.dialog-error'))dialog.querySelector('.dialog-error').textContent='';dialog.showModal(); }
$('login-form').addEventListener('submit',event=>{event.preventDefault();const form=event.currentTarget;
  action(form.querySelector('button'),async()=>{const credentials=Object.fromEntries(new FormData(form));const result=await api('/api/auth/login',{method:'POST',body:credentials});authenticate(result.data.accessToken);setRoles(result.data.roles ?? []);form.elements.password.value='';await refresh();$('login').hidden=true;$('workspace').hidden=false;$('sign-out').hidden=false;$('notice').hidden=true;});});
$('sign-out').addEventListener('click',signOut);
$('search').addEventListener('input',renderRows);
$('new-shipment').addEventListener('click',()=>openDialog('shipment-dialog'));
for(const close of document.querySelectorAll('.close-dialog')) close.addEventListener('click',()=>close.closest('dialog').close());
$('shipment-form').addEventListener('submit',event=>{event.preventDefault();const form=event.currentTarget;
  action(form.querySelector('[type=submit]'),async()=>{const body=Object.fromEntries(new FormData(form));body.weightKg=Number(body.weightKg);const created=await api('/api/shipments',{method:'POST',headers:{'Idempotency-Key':crypto.randomUUID()},body});$('shipment-dialog').close();await refresh(created.data.id);notice('Shipment created. Record evidence to include it in operational metrics.');},$('shipment-dialog'));});
$('record-event').addEventListener('click',()=>{if(!selectedId)return notice('Select a shipment first.',true);if(!$('event-type').value)return notice('Choose an event type.',true);$('event-form').elements.type.value=$('event-type').value;$('event-form').elements.referenceId.value=$('event-type').value.startsWith('hold_')?'customs':'visit';openDialog('event-dialog');});
$('event-form').addEventListener('submit',event=>{event.preventDefault();const form=event.currentTarget;
  action(form.querySelector('[type=submit]'),async()=>{const body=Object.fromEntries(new FormData(form));body.eventId=crypto.randomUUID();await api(`/api/shipments/${selectedId}/events`,{method:'POST',body});$('event-dialog').close();await refresh();notice('Evidence recorded. Metrics use the selected scenario clock.');},$('event-dialog'));});
$('open-upload').addEventListener('click',()=>{if(selectedId)openDialog('upload-dialog');});
$('upload-form').addEventListener('submit',event=>{event.preventDefault();
  action(event.currentTarget.querySelector('button'),async()=>{if(!selectedId)throw new Error('Select a shipment first.');const form=new FormData();form.append('file',$('document-file').files[0]);await api(`/api/shipments/${selectedId}/documents`,{method:'POST',body:form});$('document-file').value='';$('upload-dialog').close();await refresh();notice('Document uploaded.');},$('upload-dialog'));});
$('status-button').addEventListener('click',()=>{if(selectedId)openDialog('status-dialog');});
$('status-form').addEventListener('submit',event=>{event.preventDefault();const form=event.currentTarget;
  action(form.querySelector('[type=submit]'),async()=>{const body=Object.fromEntries(new FormData(form));body.newStatus=Number(body.newStatus);await api(`/api/shipments/${selectedId}/status`,{method:'PUT',body});$('status-dialog').close();await refresh();notice('Shipment status updated.');},$('status-dialog'));});
$('apply-clock').addEventListener('click',()=>action($('apply-clock'),async()=>{calculateMetrics(shipments,$('clock').value);clock=$('clock').value;renderMetrics();notice('Metrics recalculated at the explicit scenario clock.');}));
const tourLabels=['Start guided scenario','Record arrival','Open hold','Release and depart','Review KPIs'];
$('tour-next').addEventListener('click',()=>action($('tour-next'),async()=>{
  if(tour.step>=5)resetTour();
  const id=await advanceTour();await refresh(id);
  document.querySelectorAll('.steps li').forEach((li,index)=>{li.classList.toggle('complete',index<tour.step);li.classList.toggle('active',index===tour.step);});
  $('tour-next').textContent=tourLabels[tour.step] ?? 'Start another scenario';
  $('tour-progress').textContent=tour.step===5?'Scenario complete: source evidence, released hold, PDF manifest and delivered shipment are recorded. Review the denominator note alongside the KPIs.':`Step ${tour.step} of 5 complete. Continue with ${tourLabels[tour.step].toLowerCase()}.`;
  if(tour.step===5)notice('Guided scenario complete. Every step used the real API.');
}));

for(const link of document.querySelectorAll('nav a'))link.addEventListener('click',()=>document.querySelectorAll('nav a').forEach(other=>other.classList.toggle('active',other===link)));
