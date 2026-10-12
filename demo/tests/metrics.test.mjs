import {test} from 'node:test';
import assert from 'node:assert/strict';
import {calculateMetrics} from '../../src/WebAPI/wwwroot/metrics.js';
const at=time=>`2026-10-12T${time}:00Z`;
function event(id,type,time,correlationId='visit',observed=time){return {referenceId:id,correlationId,event:{type,occurredAt:at(time),evidence:{source:'synthetic-fixture',observedAt:at(observed),id,detail:type}}};}
const rows=[
{id:'spare',events:[event('plan','arrival_plan','08:00'),event('in','gate_in','08:20'),event('hold','hold_opened','08:30','customs')]},
{id:'equipment',events:[event('plan','arrival_plan','05:00'),event('in','gate_in','05:04'),event('out','gate_out','08:04')]},
{id:'pending',events:[event('plan','arrival_plan','10:00')]},
];
test('API envelope mapping reproduces the documented baseline',()=>{
const result=calculateMetrics(rows,at('12:00'));assert.equal(result.punctuality.rate,0.5);assert.equal(result.classification.overdue,1);assert.equal(result.dwell.meanHours,3);assert.equal(result.openHolds,1);
});
test('hold correlation groups opening and release even when source IDs differ',()=>{
const changed=structuredClone(rows);changed[0].events.push(event('release','hold_released','09:00','customs'));assert.equal(calculateMetrics(changed,at('12:00')).openHolds,0);
});
test('as-of evaluation excludes evidence observed after the selected clock',()=>{
const result=calculateMetrics(rows,at('08:10'));assert.equal(result.punctuality.evaluated,1);assert.equal(result.openHolds,0);assert.equal(result.classification.overdue,1);
});
test('a future actual timestamp does not become a completed arrival',()=>{
const result=calculateMetrics([{id:'future',events:[event('plan','arrival_plan','08:00'),event('in','gate_in','13:00','visit','09:00')]}],at('12:00'));
assert.equal(result.classification.futureActualExcluded,1);assert.equal(result.punctuality.rate,null);assert.equal(result.classification.overdue,1);
});
test('empty data stays unknown and an invalid clock gives an explicit error',()=>{
assert.equal(calculateMetrics([],at('12:00')).dwell.meanHours,null);assert.throws(()=>calculateMetrics(rows,'2026-10-12T12:00:00'));
});

test('conflicting latest plans are excluded rather than resolved by arbitrary source IDs',()=>{
 const result=calculateMetrics([{id:'conflict',events:[event('a','arrival_plan','08:00','visit','07:00'),event('b','arrival_plan','09:00','visit','07:00'),event('in','gate_in','08:20')]}],at('12:00'));
 assert.equal(result.punctuality.rate,null);assert.equal(result.punctuality.excluded,1);assert.equal(result.warnings.length,1);
});
