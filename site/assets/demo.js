// Shipment Desk's guided scenario, recomputed with Logistics KPI Utils 0.2.0 (kpi.js is the released
// library file, unchanged). Baseline and steps follow docs/DEMO.md. Evidence becomes visible two
// minutes after it happens, as in the API; planning evidence is observed at 04:00.
import { onTimeRate, summarizeDwell, summarizeArrivalStatus } from './kpi.js';

const T = hm => `2026-10-12T${hm}:00Z`;
const BASE = [
  { id: 'Spare parts', plan: '08:00', events: [['08:20', 'gate-in', 'Gate arrival'], ['08:30', 'hold', 'Customs hold opened']] },
  { id: 'Synthetic equipment', plan: '05:00', events: [['05:04', 'gate-in', 'Gate arrival'], ['08:04', 'gate-out', 'Gate departure']] },
  { id: 'Pending arrival', plan: '10:00', events: [] },
];
const GUIDED = [
  ['08:20', 'gate-in', 'Gate arrival, 20 min after plan'],
  ['08:30', 'hold', 'Customs hold opened · manifest PDF uploaded'],
  ['09:00', 'release', 'Customs hold released'],
  ['10:20', 'gate-out', 'Gate departure · status Delivered'],
];
const STEPS = [
  ['Baseline', 'Three synthetic shipments loaded from SQL. 50% on time over two completed arrivals.', 0],
  ['1 · Create shipment', 'A new shipment is created with an idempotency key and an 08:00 plan. A plan alone is not evidence of arrival.', 0],
  ['2 · Record arrival', 'Gate-in at 08:20: twenty minutes late against a five-minute tolerance.', 1],
  ['3 · Open hold', 'A customs hold at 08:30 and a manifest upload. Open holds are counted separately from punctuality.', 2],
  ['4 · Release and depart', 'Hold released at 09:00, departure at 10:20: a two-hour visit.', 4],
  ['5 · Review KPIs', 'Punctuality is now 33.3% (one of three) and mean dwell 2.5 h. The baseline hold and overdue arrival are unchanged.', 4],
];
const OBS = 2 * 60e3; // observation lag
let step = 0;
const $ = id => document.getElementById(id);
const min2hm = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

function shipments() {
  const list = BASE.map(s => ({ ...s }));
  if (step >= 1) list.push({ id: 'Guided spare parts', plan: '08:00', events: GUIDED.slice(0, STEPS[step][2]), guided: true });
  return list;
}

function render() {
  const clockMin = +$('sd-clock').value, clock = Date.parse(T(min2hm(clockMin)));
  $('sd-clockv').textContent = min2hm(clockMin) + 'Z';
  const seen = (hm) => Date.parse(T(hm)) + OBS <= clock;
  const planSeen = clock >= Date.parse(T('04:00'));
  const rows = shipments().map(s => {
    const ev = s.events.filter(e => seen(e[0]));
    const at = k => (ev.find(e => e[1] === k) || [])[0];
    const holdOpen = ev.some(e => e[1] === 'hold') && !ev.some(e => e[1] === 'release');
    return { s, ev, gin: at('gate-in'), gout: at('gate-out'), holdOpen };
  });
  const arrivals = rows.map(r => ({ id: r.s.id, planned: planSeen ? T(r.s.plan) : null, actual: r.gin ? T(r.gin) : null }));
  const visits = rows.filter(r => r.gin).map(r => ({ id: r.s.id, gateIn: T(r.gin), gateOut: r.gout ? T(r.gout) : null }));
  const st = summarizeArrivalStatus(arrivals, { asOf: T(min2hm(clockMin)), toleranceMinutes: 5 });
  const done = new Set(st.states.filter(x => x.status === 'completed').map(x => x.id));
  const ot = onTimeRate(arrivals.filter(a => done.has(a.id)), { toleranceMinutes: 5 });
  const dw = summarizeDwell(visits);
  const holds = rows.filter(r => r.holdOpen).length;
  const t = (v, l, c = '') => `<div class="kpi-tile ${c}"><div class="v">${v}</div><div class="l">${l}</div></div>`;
  $('sd-tiles').innerHTML =
    t(ot.rate === null ? '—' : (Math.round(ot.rate * 1000) / 10) + '%', `completed on time · ${ot.onTime} of ${ot.evaluated}`, 'big') +
    t(st.overdue, 'overdue arrivals', st.overdue ? 'warn' : '') +
    t(dw.meanHours === null ? '—' : (Math.round(dw.meanHours * 10) / 10) + ' h', `mean dwell · ${dw.evaluated} closed, ${dw.excluded} open`) +
    t(holds, 'open holds', holds ? 'warn' : '');
  const label = { completed: ['Arrived', 'ok'], pending: ['Expected', 'info'], overdue: ['Overdue', 'warn'], unscheduled: ['No plan yet', 'info'] };
  $('sd-table').innerHTML = '<tr><th>Shipment</th><th>Plan</th><th>Gate in</th><th>Gate out</th><th>Arrival</th></tr>' + rows.map((r, i) => {
    const [txt, cls] = label[st.states[i].status];
    return `<tr class="${r.s.guided ? 'sd-new' : ''}"><td>${r.s.id}${r.holdOpen ? ' <span class="pill bad">hold</span>' : ''}</td><td class="num">${r.s.plan}</td><td class="num">${r.gin || '—'}</td><td class="num">${r.gout || '—'}</td><td><span class="pill ${cls}">${txt}</span></td></tr>`;
  }).join('');
  const focus = rows.find(r => r.s.guided) || rows[0];
  $('sd-tl-name').textContent = focus.s.id;
  const all = [[focus.s.plan, 'plan', 'Planned arrival']].concat(focus.s.events);
  $('sd-tl').innerHTML = all.map(e => {
    const vis = e[1] === 'plan' ? planSeen : seen(e[0]);
    return `<li class="${vis ? '' : 'future'}"><span class="mono">${e[0]}</span> ${e[2]}${vis ? '' : ' <span class="dim small">· not yet observed at this clock</span>'}</li>`;
  }).join('');
  $('sd-explain').textContent = STEPS[step][1];
}

$('sd-steps').innerHTML = STEPS.map((s, i) => `<button type="button" data-i="${i}">${s[0]}</button>`).join('');
$('sd-steps').addEventListener('click', e => {
  const b = e.target.closest('button'); if (!b) return;
  step = +b.dataset.i; $('sd-clock').value = 720;
  document.querySelectorAll('#sd-steps button').forEach(x => x.setAttribute('aria-selected', x === b));
  render();
});
$('sd-clock').addEventListener('input', render);
document.querySelector('#sd-steps button').setAttribute('aria-selected', 'true');
render();
