import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, interval, catchError, of, switchMap } from 'rxjs';
import { ApiService } from './api.service';

export enum ConnectionStatus {
  CONNECTED = 'connected',
  DISCONNECTED = 'disconnected',
  CONNECTING = 'connecting',
  ERROR = 'error'
}

export interface ConnectionState {
  status: ConnectionStatus;
  lastChecked: Date;
  errorMessage?: string;
}

@Injectable({
  providedIn: 'root'
})
export class ConnectionStatusService {
  private connectionState$ = new BehaviorSubject<ConnectionState>({
    status: ConnectionStatus.DISCONNECTED,
    lastChecked: new Date()
  });

  private checkInterval = 15000; // Check every 15 seconds
  private isChecking = false;

  constructor(private apiService: ApiService) {
    this.startPeriodicCheck();
  }

  get status$(): Observable<ConnectionState> {
    return this.connectionState$.asObservable();
  }

  get currentStatus(): ConnectionState {
    return this.connectionState$.value;
  }

  async checkConnection(): Promise<void> {
    if (this.isChecking) return;
    
    this.isChecking = true;
    this.updateStatus(ConnectionStatus.CONNECTING); // Don't pass errorMessage, preserve existing one

    try {
      const result = await this.apiService.getData().pipe(
        catchError(error => {
          console.error('Connection check failed:', error);
          this.updateStatus(ConnectionStatus.ERROR, error.message);
          this.isChecking = false;
          return of(null);
        })
      ).toPromise();

      // Only set as connected if we got a valid result (not null from catchError)
      if (result !== null) {
        this.updateStatus(ConnectionStatus.CONNECTED, undefined); // Clear error message
      }
    } catch (error: any) {
      this.updateStatus(ConnectionStatus.ERROR, error.message);
    } finally {
      this.isChecking = false;
    }
  }

  private startPeriodicCheck(): void {
    // Initial check
    this.checkConnection();

    // Set up periodic checks
    interval(this.checkInterval).pipe(
      switchMap(() => {
        this.checkConnection();
        return of(null);
      })
    ).subscribe();
  }

  private updateStatus(status: ConnectionStatus, errorMessage?: string): void {
    const currentState = this.connectionState$.value;
    const newState: ConnectionState = {
      status,
      lastChecked: new Date(),
      errorMessage: errorMessage !== undefined ? errorMessage : currentState.errorMessage
    };

    // Only emit if something meaningful changed (status or error message)
    if (currentState.status !== status || currentState.errorMessage !== newState.errorMessage) {
      this.connectionState$.next(newState);
    } else {
      // Only update timestamp for connection attempts without changing other state
      this.connectionState$.next({
        ...currentState,
        lastChecked: new Date()
      });
    }
  }

  retryConnection(): void {
    this.checkConnection();
  }
}
