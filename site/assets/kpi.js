/** Dependency-free logistics KPI calculations. See README for metric definitions. */
const STAMP = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,3})?(Z|[+-]\d{2}:\d{2})$/;
function timestamp(value, label) {
  if (value instanceof Date) {
    const result = value.getTime();
    if (!Number.isFinite(result)) throw new TypeError(`${label} must be a valid Date`);
    return result;
  }
  if (typeof value !== 'string') throw new TypeError(`${label} must be an ISO timestamp with timezone or a Date`);
  const match = STAMP.exec(value);
  if (!match) throw new TypeError(`${label} must include a date, time and explicit timezone`);
  const [, yy, mm, dd, hh, mi, ss, zone] = match;
  const y = Number(yy), m = Number(mm), d = Number(dd);
  const calendar = new Date(0);
  calendar.setUTCFullYear(y, m - 1, d);
  calendar.setUTCHours(0, 0, 0, 0);
  if (m < 1 || m > 12 || d < 1 || calendar.getUTCMonth() !== m - 1 || calendar.getUTCDate() !== d ||
      Number(hh) > 23 || Number(mi) > 59 || Number(ss) > 59 ||
      (zone !== 'Z' && (Number(zone.slice(1, 3)) > 23 || Number(zone.slice(4)) > 59)))
    throw new RangeError(`${label} contains an invalid calendar date, time or offset`);
  const result = Date.parse(value);
  if (!Number.isFinite(result)) throw new RangeError(`${label} is not a valid timestamp`);
  return result;
}
function records(value) {
  if (!Array.isArray(value)) throw new TypeError('records must be an array');
  const seen = new Set();
  for (const row of value) {
    if (!row || typeof row !== 'object' || typeof row.id !== 'string' || row.id.trim() === '')
      throw new TypeError('every record must have a non-empty string id');
    if (seen.has(row.id)) throw new RangeError(`duplicate record id: ${row.id}`);
    seen.add(row.id);
  }
}
const missing = value => value === null || value === undefined;

/** Elapsed hours between gate-in and gate-out. Negative dwell is rejected. */
export function dwellHours(gateIn, gateOut) {
  const start = timestamp(gateIn, 'gateIn'), end = timestamp(gateOut, 'gateOut');
  if (end < start) throw new RangeError('gateOut precedes gateIn');
  return (end - start) / 3_600_000;
}
/** Signed minutes: actual minus planned. Negative means early. */
export function delayMinutes(planned, actual) {
  return (timestamp(actual, 'actual') - timestamp(planned, 'planned')) / 60_000;
}
/** Completed-record rate only. Missing arrivals are reported and excluded. */
export function onTimeRate(arrivals, { toleranceMinutes = 0 } = {}) {
  records(arrivals);
  if (!Number.isFinite(toleranceMinutes) || toleranceMinutes < 0)
    throw new RangeError('toleranceMinutes must be finite and non-negative');
  let evaluated = 0, onTime = 0, excluded = 0;
  for (const row of arrivals) {
    // Validate provided values even when their counterpart is missing.
    if (!missing(row.planned)) timestamp(row.planned, 'planned');
    if (!missing(row.actual)) timestamp(row.actual, 'actual');
    if (missing(row.planned) || missing(row.actual)) { excluded++; continue; }
    evaluated++;
    if (delayMinutes(row.planned, row.actual) <= toleranceMinutes) onTime++;
  }
  return { total: arrivals.length, evaluated, excluded, onTime, late: evaluated - onTime,
    rate: evaluated === 0 ? null : onTime / evaluated };
}
/** Completed visits only. Percentiles use linear interpolation (R-7). */
export function summarizeDwell(visits) {
  records(visits);
  const values = [];
  let excluded = 0;
  for (const row of visits) {
    if (!missing(row.gateIn)) timestamp(row.gateIn, 'gateIn');
    if (!missing(row.gateOut)) timestamp(row.gateOut, 'gateOut');
    if (missing(row.gateIn) || missing(row.gateOut)) { excluded++; continue; }
    values.push(dwellHours(row.gateIn, row.gateOut));
  }
  values.sort((a, b) => a - b);
  function percentile(p) {
    if (!values.length) return null;
    const index = (values.length - 1) * p, low = Math.floor(index), high = Math.ceil(index);
    return values[low] + (values[high] - values[low]) * (index - low);
  }
  return { total: visits.length, evaluated: values.length, excluded,
    meanHours: values.length ? values.reduce((sum, x) => sum + x / values.length, 0) : null,
    medianHours: percentile(0.5), p95Hours: percentile(0.95),
    minHours: values[0] ?? null, maxHours: values.at(-1) ?? null };
}

/** Classify arrivals at an explicit as-of instant. Future actual times are not completed evidence. */
export function summarizeArrivalStatus(arrivals, { asOf, toleranceMinutes = 0 } = {}) {
  records(arrivals);
  const cutoff = timestamp(asOf, 'asOf');
  if (!Number.isFinite(toleranceMinutes) || toleranceMinutes < 0)
    throw new RangeError('toleranceMinutes must be finite and non-negative');
  const counts = { completed: 0, pending: 0, overdue: 0, unscheduled: 0, futureActualExcluded: 0 };
  const states = arrivals.map(row => {
    const planned = missing(row.planned) ? null : timestamp(row.planned, 'planned');
    const actual = missing(row.actual) ? null : timestamp(row.actual, 'actual');
    const futureActual = actual !== null && actual > cutoff;
    if (futureActual) counts.futureActualExcluded++;
    const status = actual !== null && actual <= cutoff ? 'completed'
      : planned === null ? 'unscheduled'
      : cutoff > planned + toleranceMinutes * 60_000 ? 'overdue' : 'pending';
    counts[status]++;
    return { id: row.id, status, futureActual };
  });
  return { total: arrivals.length, asOf: new Date(cutoff).toISOString(), ...counts, states };
}
