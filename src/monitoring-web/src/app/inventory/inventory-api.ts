import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { SESSION_DETAIL_API_ORIGIN } from '../session-detail/session-detail-api';

export interface ScopePair {
  siteId: string;
  sensorId: string;
}

export interface DeviceView {
  deviceId: string;
  name: string;
  description: string;
  revision: number;
  scopes: ScopePair[];
  updatedAt: string;
}

export interface IpAssociation {
  ip: string;
  firstSeen: string;
  lastSeen: string;
}

export type CandidateState = 'candidate' | 'confirmed' | 'rejected';

export interface CandidateView {
  candidateId: string;
  siteId: string;
  sensorId: string;
  mac: string;
  vlanId: number | null;
  firstSeen: string;
  lastSeen: string;
  state: CandidateState;
  revision: number;
  deviceId: string | null;
  associations: IpAssociation[];
}

export interface ObservationView {
  siteId: string;
  sensorId: string;
  eventId: string;
  observedAt: string;
  ip: string;
  mac: string | null;
  vlanId: number | null;
}

export interface InventoryPage<T> {
  items: T[];
  nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class InventoryApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(SESSION_DETAIL_API_ORIGIN)}/api/v1/inventory`;

  listDevices(cursor: string | null): Observable<InventoryPage<DeviceView>> {
    return this.http.get<InventoryPage<DeviceView>>(`${this.base}/devices`, { params: this.params(cursor) });
  }

  listCandidates(state: CandidateState | null, cursor: string | null): Observable<InventoryPage<CandidateView>> {
    let params = this.params(cursor);
    if (state !== null) params = params.set('state', state);
    return this.http.get<InventoryPage<CandidateView>>(`${this.base}/candidates`, { params });
  }

  listObservations(cursor: string | null): Observable<InventoryPage<ObservationView>> {
    return this.http.get<InventoryPage<ObservationView>>(`${this.base}/observations`, { params: this.params(cursor) });
  }

  confirm(id: string, expectedRevision: number, deviceName: string, deviceDescription: string): Observable<CandidateView> {
    return this.http.post<CandidateView>(`${this.base}/candidates/${encodeURIComponent(id)}/confirm`, { expectedRevision, deviceName, deviceDescription });
  }

  reject(id: string, expectedRevision: number, reason: string): Observable<CandidateView> {
    return this.http.post<CandidateView>(`${this.base}/candidates/${encodeURIComponent(id)}/reject`, { expectedRevision, reason });
  }

  merge(id: string, expectedRevision: number, deviceId: string, expectedDeviceRevision: number): Observable<CandidateView> {
    return this.http.post<CandidateView>(`${this.base}/candidates/${encodeURIComponent(id)}/merge`, { expectedRevision, deviceId, expectedDeviceRevision });
  }

  updateDevice(id: string, expectedRevision: number, name: string, description: string): Observable<DeviceView> {
    return this.http.patch<DeviceView>(`${this.base}/devices/${encodeURIComponent(id)}`, { expectedRevision, name, description });
  }

  private params(cursor: string | null): HttpParams {
    let params = new HttpParams().set('pageSize', 50);
    if (cursor) params = params.set('cursor', cursor);
    return params;
  }
}

export function inventoryStatus(error: unknown): number {
  return error instanceof HttpErrorResponse ? error.status : 0;
}

// Plain-language text for every refusal the API can give; none echoes server detail.
export function inventoryErrorMessage(status: number): string {
  switch (status) {
    case 400:
      return 'Datos no válidos. Revisa los campos e inténtalo de nuevo.';
    case 401:
      return 'Inicia sesión con una identidad válida para ver el inventario.';
    case 403:
      return 'Acceso denegado. No tienes permiso para esta operación o para estos ámbitos.';
    case 404:
      return 'El registro ya no existe. Recarga la lista.';
    case 409:
      return 'Otro usuario ha cambiado este registro. Se ha recargado la lista; revisa los datos y repite la operación.';
    case 410:
      return 'La lista ha caducado. Se ha reiniciado desde el principio.';
    case 413:
      return 'Los datos enviados son demasiado grandes.';
    case 503:
      return 'Inventario no disponible en este momento.';
    case 504:
      return 'Se agotó el tiempo de espera. Reintenta.';
    default:
      return 'No se pudo conectar con el servicio. Reintenta.';
  }
}
