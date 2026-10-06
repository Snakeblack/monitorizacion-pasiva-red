import { recentSessionFilters, SessionFilters, sessionFilterError } from './session-query';

const now = Date.parse('2026-10-05T12:00:00.123Z');
const short: SessionFilters = {
  from: '2026-10-04T12:00:00.123Z',
  to: '2026-10-05T12:00:00.123Z',
  siteId: '',
  sensorId: '',
  sourceIp: '',
  destinationIp: '',
  protocol: '',
  sourcePort: '',
  destinationPort: '',
  pageSize: 50,
};

describe('Session query boundaries', () => {
  it('defaults to precisely the recent 24 UTC hours and page 50 without optional filters', () => {
    expect(recentSessionFilters(now)).toEqual(short);
    expect(sessionFilterError(short, now)).toBe('');
  });

  it.each([
    { patch: { from: '' }, message: 'UTC' },
    { patch: { from: '2026-10-04T12:00:00+00:00' }, message: 'UTC' },
    { patch: { from: '2026-10-04T12:00:00.1234Z' }, message: 'UTC' },
    { patch: { from: '2026-02-30T12:00:00Z' }, message: 'UTC' },
    { patch: { from: short.to }, message: 'anterior' },
    { patch: { to: '2026-10-05T12:00:01Z' }, message: 'futuro' },
    { patch: { from: '2026-09-04T12:00:00Z' }, message: '30 días' },
    { patch: { from: '2026-10-03T12:00:00Z' }, message: 'sede, sonda y una IP' },
    {
      patch: { from: '2026-10-03T12:00:00Z', siteId: 'a', sourceIp: '192.0.2.1' },
      message: 'sede, sonda y una IP',
    },
    { patch: { pageSize: 101 }, message: '1–100' },
    { patch: { pageSize: 0 }, message: '1–100' },
    { patch: { pageSize: 1.5 }, message: '1–100' },
    { patch: { sourceIp: '999.0.0.1' }, message: 'IP' },
    { patch: { destinationIp: '2001:db8::xyz' }, message: 'IP' },
    { patch: { protocol: 'ICMP' }, message: 'TCP o UDP' },
    { patch: { sourcePort: '-1' }, message: '0–65535' },
    { patch: { destinationPort: '65536' }, message: '0–65535' },
    { patch: { sourcePort: '1.5' }, message: '0–65535' },
  ])('rejects $patch before HTTP', ({ patch, message }) => {
    expect(sessionFilterError({ ...short, ...patch }, now)).toContain(message);
  });

  it('accepts a selective 30-day interval, IPv6 and boundary ports without inventing data', () => {
    expect(
      sessionFilterError(
        {
          ...short,
          from: '2026-09-05T12:00:00.123Z',
          siteId: 'a',
          sensorId: 'b',
          destinationIp: '2001:db8::2',
          protocol: 'UDP',
          sourcePort: '0',
          destinationPort: '65535',
          pageSize: 100,
        },
        now,
      ),
    ).toBe('');
  });
});
