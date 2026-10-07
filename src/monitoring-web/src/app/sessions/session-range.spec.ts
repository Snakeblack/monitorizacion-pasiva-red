import { normalizeUtcInput, RANGE_PRESETS, rangeEndingAt } from './session-range';
import { recentSessionFilters, sessionFilterError } from './session-query';

const now = Date.parse('2026-10-05T12:00:00.123Z');

describe('Range presets', () => {
  it('offers the ranges an analyst reaches for first, shortest to longest, and a 24 h one the recent-sessions default agrees with', () => {
    expect(RANGE_PRESETS.map((preset) => preset.id)).toEqual(['15m', '1h', '6h', '24h', '7d', '30d']);
    expect(rangeEndingAt(now, '24h')).toEqual({ from: recentSessionFilters(now).from, to: recentSessionFilters(now).to });
  });

  it.each([
    ['15m', '2026-10-05T11:45:00.123Z'],
    ['1h', '2026-10-05T11:00:00.123Z'],
    ['6h', '2026-10-05T06:00:00.123Z'],
    ['7d', '2026-09-28T12:00:00.123Z'],
  ] as const)('ends now and starts a fixed distance before it (%s)', (id, from) => {
    expect(rangeEndingAt(now, id)).toEqual({ from, to: '2026-10-05T12:00:00.123Z' });
  });

  it('keeps the 30-day preset inside the limit for the seconds a person takes to press search', () => {
    const range = rangeEndingAt(now, '30d');
    const filters = { ...recentSessionFilters(now), ...range, siteId: 'a', sensorId: 'b', sourceIp: '192.0.2.1' };
    expect(sessionFilterError(filters, now + 120_000)).toBe('');
  });

  it('is rejected by the existing rules when it is longer than a day and no site, sensor and address narrow it', () => {
    const filters = { ...recentSessionFilters(now), ...rangeEndingAt(now, '7d') };
    expect(sessionFilterError(filters, now)).toContain('sede, sonda y una IP');
  });
});

describe('Typed UTC dates', () => {
  it.each([
    ['2026-10-05 12:00', '2026-10-05T12:00:00Z'],
    ['2026-10-05T12:00', '2026-10-05T12:00:00Z'],
    ['2026-10-05 12:00:30', '2026-10-05T12:00:30Z'],
    ['2026-10-05T12:00:30Z', '2026-10-05T12:00:30Z'],
    ['2026-10-05 12:00:30.5', '2026-10-05T12:00:30.5Z'],
    ['2026-10-05', '2026-10-05T00:00:00Z'],
    ['  2026-10-05   12:00  ', '2026-10-05T12:00:00Z'],
  ])('turns %j into the canonical form the API takes', (typed, canonical) => {
    expect(normalizeUtcInput(typed)).toBe(canonical);
  });

  it.each(['', 'ayer', '2026-13-40 12:00', '2026-02-30 10:00', '2026-10-05 25:00', '2026-10-05 12:61', '05/10/2026 12:00', '2026-10-05T12:00:00+02:00'])(
    'does not guess what %j means',
    (typed) => {
      expect(normalizeUtcInput(typed)).toBeNull();
    },
  );
});
