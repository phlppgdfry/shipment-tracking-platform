import { onTimeRate, summarizeDwell, summarizeArrivalStatus } from './vendor/logistics-kpi-utils/index.js';
export function calculateMetrics(shipments, asOf, toleranceMinutes = 5) {
  // Validate the explicit clock using the package before any local filtering.
  summarizeArrivalStatus([], { asOf, toleranceMinutes });
  const instant = asOf instanceof Date ? asOf.getTime() : Date.parse(asOf);
  const arrivals = [], visits = [], warnings = []; let openHolds = 0;
  for (const row of shipments) {
    const events = row.events.filter(e => Date.parse(e.event.evidence.observedAt) <= instant);
    const latest = type => events.filter(e => e.event.type === type)
      .sort((a,b) => Date.parse(b.event.evidence.observedAt)-Date.parse(a.event.evidence.observedAt) || b.referenceId.localeCompare(a.referenceId))[0];
    let plan = latest('arrival_plan');
    if (plan) {
      const candidates = events.filter(e=>e.event.type==='arrival_plan' && e.event.evidence.observedAt===plan.event.evidence.observedAt);
      if (new Set(candidates.map(e=>Date.parse(e.event.occurredAt))).size>1) {
        warnings.push(`Conflicting latest plans for ${row.trackingNumber ?? row.id}; plan excluded.`); plan=null;
      }
    }
    const arrival = latest('gate_in'), departure = latest('gate_out');
    arrivals.push({ id: row.id, planned: plan?.event.occurredAt ?? null, actual: arrival?.event.occurredAt ?? null });
    visits.push({ id: row.id, gateIn: arrival && Date.parse(arrival.event.occurredAt)<=instant ? arrival.event.occurredAt : null,
      gateOut: departure && Date.parse(departure.event.occurredAt)<=instant ? departure.event.occurredAt : null });
    const holds = new Map();
    for (const e of events.filter(e => e.event.type.startsWith('hold_') && Date.parse(e.event.occurredAt)<=instant)
      .sort((a,b) => Date.parse(a.event.occurredAt)-Date.parse(b.event.occurredAt) || Date.parse(a.event.evidence.observedAt)-Date.parse(b.event.evidence.observedAt)))
      holds.set(e.correlationId ?? e.referenceId, e.event.type);
    openHolds += [...holds.values()].filter(type=>type==='hold_opened').length;
  }
  const classification = summarizeArrivalStatus(arrivals, { asOf, toleranceMinutes });
  const completed = new Set(classification.states.filter(e=>e.status==='completed').map(e=>e.id));
  return { classification, punctuality: onTimeRate(arrivals.filter(e=>completed.has(e.id)), {toleranceMinutes}),
    dwell: summarizeDwell(visits), openHolds, warnings };
}
