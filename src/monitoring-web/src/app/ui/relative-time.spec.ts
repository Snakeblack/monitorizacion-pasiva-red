import { relativeTime } from './relative-time';

const now = Date.parse('2026-10-05T12:00:00Z');
const ago = (ms: number) => new Date(now - ms).toISOString();

describe('Relative time', () => {
  it.each([
    [0, 'hace unos segundos'],
    [20_000, 'hace unos segundos'],
    [90_000, 'hace 1 min'],
    [5 * 60_000, 'hace 5 min'],
    [59 * 60_000, 'hace 59 min'],
    [60 * 60_000, 'hace 1 h'],
    [23 * 3_600_000, 'hace 23 h'],
    [24 * 3_600_000, 'hace 1 d'],
    [10 * 24 * 3_600_000, 'hace 10 d'],
  ])('describes %i ms ago as %j', (offset, expected) => {
    expect(relativeTime(ago(offset), now)).toBe(expected);
  });

  it('treats a moment slightly in the future (clock skew between sensor and browser) as just now', () => {
    expect(relativeTime(new Date(now + 5_000).toISOString(), now)).toBe('hace unos segundos');
  });

  it('shows the date itself once it is too old to read as a distance', () => {
    expect(relativeTime('2026-08-01T10:00:00Z', now)).toBe('2026-08-01');
  });

  it('hands back what it cannot read instead of inventing a time', () => {
    expect(relativeTime('no es una fecha', now)).toBe('no es una fecha');
  });
});
