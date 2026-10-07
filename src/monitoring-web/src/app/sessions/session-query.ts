export interface SessionFilters {
  from: string;
  to: string;
  siteId: string;
  sensorId: string;
  sourceIp: string;
  destinationIp: string;
  protocol: string;
  sourcePort: string;
  destinationPort: string;
  pageSize: number;
}

const day = 24 * 60 * 60 * 1000;

export function recentSessionFilters(now: number): SessionFilters {
  return {
    from: new Date(now - day).toISOString(),
    to: new Date(now).toISOString(),
    siteId: '',
    sensorId: '',
    sourceIp: '',
    destinationIp: '',
    protocol: '',
    sourcePort: '',
    destinationPort: '',
    pageSize: 50,
  };
}

export function sessionFilterError(filters: SessionFilters, now: number): string {
  const from = utcTime(filters.from);
  const to = utcTime(filters.to);
  if (!Number.isFinite(from) || !Number.isFinite(to)) {
    return 'Introduce fechas UTC con Z y precisión de hasta milisegundos.';
  }
  if (from >= to) return 'El inicio debe ser anterior al fin del intervalo.';
  if (to > now) return 'El fin no puede estar en el futuro.';
  if (to - from > 30 * day || from < now - 30 * day) {
    return 'Consulta como máximo 30 días dentro de los últimos 30 días.';
  }
  if (
    to - from > day &&
    (!filters.siteId || !filters.sensorId || (!filters.sourceIp && !filters.destinationIp))
  ) {
    return 'Para más de 24 h indica sede, sonda y una IP de origen o destino.';
  }
  if (!Number.isInteger(filters.pageSize) || filters.pageSize < 1 || filters.pageSize > 100) {
    return 'El tamaño de página debe ser un entero de 1–100.';
  }
  if ([filters.sourceIp, filters.destinationIp].some((ip) => ip !== '' && !validIp(ip))) {
    return 'Introduce una IP válida de origen o destino; no se admite búsqueda libre.';
  }
  if (filters.protocol !== '' && !['TCP', 'UDP'].includes(filters.protocol)) {
    return 'El protocolo debe ser TCP o UDP.';
  }
  if (
    [filters.sourcePort, filters.destinationPort].some(
      (port) => port !== '' && (!/^\d+$/.test(port) || Number(port) > 65535),
    )
  ) {
    return 'Los puertos deben ser enteros de 0–65535.';
  }
  return '';
}

// Which exact-match filters are wrong, so each can show its own message beside the value instead of one sentence for the whole form.
export function sessionFieldErrors(filters: SessionFilters): Partial<Record<'sourceIp' | 'destinationIp' | 'protocol' | 'sourcePort' | 'destinationPort', string>> {
  const errors: ReturnType<typeof sessionFieldErrors> = {};
  const ipMessage = 'Escribe una dirección IPv4 o IPv6 completa; no se admite búsqueda libre.';
  if (filters.sourceIp !== '' && !validIp(filters.sourceIp)) errors.sourceIp = ipMessage;
  if (filters.destinationIp !== '' && !validIp(filters.destinationIp)) errors.destinationIp = ipMessage;
  if (filters.protocol !== '' && !['TCP', 'UDP'].includes(filters.protocol)) errors.protocol = 'El protocolo debe ser TCP o UDP.';
  const portMessage = 'Un número entero de 0–65535.';
  if (filters.sourcePort !== '' && !validPort(filters.sourcePort)) errors.sourcePort = portMessage;
  if (filters.destinationPort !== '' && !validPort(filters.destinationPort)) errors.destinationPort = portMessage;
  return errors;
}

function validPort(port: string): boolean {
  return /^\d+$/.test(port) && Number(port) <= 65535;
}

function utcTime(value: string): number {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z$/.test(value)) return NaN;
  const time = Date.parse(value);
  // Date.parse rolls impossible calendar dates forward; reject that normalization.
  if (!Number.isFinite(time) || new Date(time).toISOString().slice(0, 19) !== value.slice(0, 19))
    return NaN;
  return time;
}

function validIp(value: string): boolean {
  if (!value.includes(':')) {
    const segments = value.split('.');
    return (
      segments.length === 4 &&
      segments.every((segment) => /^(0|[1-9]\d{0,2})$/.test(segment) && Number(segment) <= 255)
    );
  }
  if (!/^[\da-fA-F:.]+$/.test(value)) return false;
  try {
    return new URL(`http://[${value}]/`).hostname.startsWith('[');
  } catch {
    return false;
  }
}
