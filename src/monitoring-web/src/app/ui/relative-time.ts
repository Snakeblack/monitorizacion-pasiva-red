// "hace 5 min" for the table, with the exact UTC instant kept next to it by the caller. Past the point where a distance reads naturally the
// date itself is shown. Unreadable input comes back as it was, so nothing is invented.
export function relativeTime(iso: string, now: number): string {
  const time = Date.parse(iso);
  if (!Number.isFinite(time)) return iso;
  const seconds = Math.max(0, Math.round((now - time) / 1000));
  if (seconds < 60) return 'hace unos segundos';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `hace ${days} d`;
  return new Date(time).toISOString().slice(0, 10);
}
