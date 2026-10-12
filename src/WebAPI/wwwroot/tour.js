import { api } from './api.js';
export const tour = { step: 0, shipmentId: null, key: null };
const occurrence = time => `2026-10-12T${time}:00Z`;
async function event(eventId, type, time, referenceId, detail) {
  const at = occurrence(time);
  return api(`/api/shipments/${tour.shipmentId}/events`, { method: 'POST', body: { eventId, type, referenceId,
    occurredAt: at, observedAt: type==='arrival_plan'?'2026-10-12T04:00:00Z':new Date(Date.parse(at) + 120000).toISOString(), source: 'synthetic-guided-tour', detail } });
}
async function status(value, notes) {
  const current = (await api(`/api/shipments/${tour.shipmentId}`)).data.status;
  if (current >= value) return;
  return api(`/api/shipments/${tour.shipmentId}/status`, { method: 'PUT', body: { newStatus: value, notes } });
}
export async function advanceTour() {
  if (tour.step === 0) {
    tour.key ??= crypto.randomUUID();
    const result = await api('/api/shipments', { method: 'POST', headers: { 'Idempotency-Key': tour.key }, body: {
      senderName:'Synthetic Sender',senderAddress:'Demo A',recipientName:'Synthetic Recipient',recipientAddress:'Demo B',description:'Guided spare parts',weightKg:12.5 } });
    tour.shipmentId = result.data.id;
    await status(1,'Synthetic booking confirmed');
    await event('tour-plan','arrival_plan','08:00','visit','Planned arrival');
  } else if (tour.step === 1) {
    await event('tour-arrival','gate_in','08:20','visit','Gate arrival recorded 20 minutes after plan');
    await status(2,'Synthetic shipment entered transit');
  } else if (tour.step === 2) {
    await event('tour-hold','hold_opened','08:30','customs','Customs hold opened; operator review required');
    const current = (await api(`/api/shipments/${tour.shipmentId}`)).data;
    if (!current.documents.some(d=>d.fileName==='demo-manifest.pdf')) {
      const pdf = await (await fetch('demo-manifest.pdf')).blob(); const form = new FormData();
      form.append('file',pdf,'demo-manifest.pdf');
      await api(`/api/shipments/${tour.shipmentId}/documents`, {method:'POST',body:form});
    }
  } else if (tour.step === 3) {
    await event('tour-release','hold_released','09:00','customs','Customs hold released after review');
    await event('tour-departure','gate_out','10:20','visit','Gate departure recorded; completed dwell is 2 hours');
    await status(3,'Synthetic shipment out for delivery');
    await status(4,'Synthetic shipment delivered');
  }
  tour.step++; return tour.shipmentId;
}
export function resetTour() { tour.step=0; tour.shipmentId=null; tour.key=null; }
