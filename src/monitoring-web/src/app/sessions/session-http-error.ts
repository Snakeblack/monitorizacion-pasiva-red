import { HttpErrorResponse } from '@angular/common/http';

export function sessionHttpStatus(error: unknown): number {
  if (error instanceof HttpErrorResponse) return error.status;
  if (error instanceof Error && error.cause instanceof HttpErrorResponse) return error.cause.status;
  return 0;
}

// `signInAvailable` is false when the console runs without an identity provider: a 401 then cannot be fixed by signing in, so the message names the cause.
export function sessionErrorMessage(status: number, signInAvailable = true): string {
  switch (status) {
    case 400:
      return 'Consulta inválida. Revisa el intervalo y los filtros y vuelve a buscar.';
    case 401:
      return signInAvailable
        ? 'Inicia sesión con una identidad válida para consultar sesiones.'
        : 'El servidor exige iniciar sesión, pero esta consola se ha arrancado sin proveedor de identidad. Arráncala con «npm run start:lab» (consulta la guía de demo).';
    case 403:
      return 'Acceso denegado. No tienes permiso para este ámbito o tus permisos han cambiado.';
    case 410:
      return 'La consulta ha caducado. Reinicia para obtener un nuevo snapshot.';
    case 429:
      return 'Demasiadas consultas simultáneas. Espera y reintenta.';
    case 503:
      return 'Búsqueda no disponible. No se puede consultar la proyección en este momento.';
    case 504:
      return 'Se agotó el tiempo de espera. Acota el intervalo y reintenta.';
    default:
      return 'No se pudo conectar con el servicio. Reintenta la consulta.';
  }
}
