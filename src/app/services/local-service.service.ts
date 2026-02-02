import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, interval, Subscription, catchError, of } from 'rxjs';

const SERVICE_API_URL = 'http://127.0.0.1:47836';

export enum ServiceConnectionStatus {
  CONNECTING = 'connecting',
  CONNECTED = 'connected',
  DISCONNECTED = 'disconnected',
  ERROR = 'error',
  SERVICE_UNAVAILABLE = 'service_unavailable'
}

export interface ServiceStatus {
  status: string;
  connectedAt: string | null;
  currentUptime: number;
  connectionHistory: number[];
}

@Injectable({
  providedIn: 'root'
})
export class LocalServiceService implements OnDestroy {
  private status$ = new BehaviorSubject<ServiceStatus | null>(null);
  private serviceAvailable$ = new BehaviorSubject<boolean>(false);
  private pollInterval$: Subscription | null = null;

  constructor(private http: HttpClient) {}

  get currentStatus$(): Observable<ServiceStatus | null> {
    return this.status$.asObservable();
  }

  get isServiceAvailable$(): Observable<boolean> {
    return this.serviceAvailable$.asObservable();
  }

  get currentStatus(): ServiceStatus | null {
    return this.status$.value;
  }

  get isServiceAvailable(): boolean {
    return this.serviceAvailable$.value;
  }

  startPolling(): void {
    if (this.pollInterval$) {
      return;
    }

    // Initial fetch
    this.fetchStatus();

    // Poll every second for real-time updates
    this.pollInterval$ = interval(1000).subscribe(() => {
      this.fetchStatus();
    });
  }

  stopPolling(): void {
    if (this.pollInterval$) {
      this.pollInterval$.unsubscribe();
      this.pollInterval$ = null;
    }
  }

  private fetchStatus(): void {
    this.http.get<ServiceStatus>(`${SERVICE_API_URL}/status`)
      .pipe(
        catchError(() => {
          this.serviceAvailable$.next(false);
          this.status$.next(null);
          return of(null);
        })
      )
      .subscribe(status => {
        if (status) {
          this.serviceAvailable$.next(true);
          this.status$.next(status);
        }
      });
  }

  checkHealth(): Observable<{ status: string; timestamp: string }> {
    return this.http.get<{ status: string; timestamp: string }>(`${SERVICE_API_URL}/health`);
  }

  forceReconnect(): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${SERVICE_API_URL}/reconnect`, {});
  }

  updateConfig(config: { apiUrl?: string; apiKey?: string; siteId?: number; tenantId?: number }): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${SERVICE_API_URL}/config`, config);
  }

  prepopulateHistory(sessions: Array<{ connected_at: string; disconnected_at: string | null }>): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${SERVICE_API_URL}/prepopulate-history`, { sessions });
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }
}
