// Time ranges for the session search: one-press presets that always end "now" when the search runs, and a lenient reader for dates typed by
// hand. Everything leaves here in the canonical UTC form the API takes; the validation rules themselves stay in session-query.ts.

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

export type RangePresetId = '15m' | '1h' | '6h' | '24h' | '7d' | '30d';

export interface RangePreset {
  id: RangePresetId;
  label: string;
  // Name for assistive technology, which should say what the range is, not just how long.
  name: string;
  milliseconds: number;
}

export const RANGE_PRESETS: readonly RangePreset[] = [
  { id: '15m', label: '15 min', name: 'Últimos 15 minutos', milliseconds: 15 * minute },
  { id: '1h', label: '1 h', name: 'Última hora', milliseconds: hour },
  { id: '6h', label: '6 h', name: 'Últimas 6 h', milliseconds: 6 * hour },
  { id: '24h', label: '24 h', name: 'Últimas 24 h', milliseconds: day },
  { id: '7d', label: '7 d', name: 'Últimos 7 días', milliseconds: 7 * day },
  // A minute short of 30 days: the limit is measured at the moment of the request, a little after the button was pressed.
  { id: '30d', label: '30 d', name: 'Últimos 30 días', milliseconds: 30 * day - 5 * minute },
];

export function rangeEndingAt(now: number, id: RangePresetId): { from: string; to: string } {
  const preset = RANGE_PRESETS.find((candidate) => candidate.id === id)!;
  return { from: new Date(now - preset.milliseconds).toISOString(), to: new Date(now).toISOString() };
}

const TYPED_UTC = /^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2})(\.\d{1,3})?)?)?Z?$/;

// Accepts what people actually type ("2026-10-05 12:00", "2026-10-05T12:00:30", a bare date) as UTC and returns the canonical
// `YYYY-MM-DDTHH:mm:ss[.fff]Z`; returns null for anything ambiguous or impossible rather than guessing (no local time, no offsets, no rolled dates).
export function normalizeUtcInput(typed: string): string | null {
  const match = TYPED_UTC.exec(typed.trim().replace(/\s+/g, ' '));
  if (!match) return null;
  const [, year, month, dayOfMonth, hours = '00', minutes = '00', seconds = '00', fraction = ''] = match;
  const canonical = `${year}-${month}-${dayOfMonth}T${hours}:${minutes}:${seconds}${fraction}Z`;
  const time = Date.parse(canonical);
  // Date.parse rolls impossible calendar dates forward (30 February); the round trip catches that.
  if (!Number.isFinite(time) || new Date(time).toISOString().slice(0, 19) !== canonical.slice(0, 19)) return null;
  return canonical;
}
