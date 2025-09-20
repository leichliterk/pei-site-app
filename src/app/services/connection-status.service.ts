import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, interval, catchError, of, switchMap, Subscription } from 'rxjs';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../environments/environment';

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
  private readonly connStatusUrl = 'http://localhost:443/api/data/conn-status';
  private timerSubscription?: Subscription;
  private _isTimerRunning$ = new BehaviorSubject<boolean>(true);

  constructor(private http: HttpClient) {
    this.startPeriodicCheck();
  }

  get status$(): Observable<ConnectionState> {
    return this.connectionState$.asObservable();
  }

  get currentStatus(): ConnectionState {
    return this.connectionState$.value;
  }

  get isTimerRunning$(): Observable<boolean> {
    return this._isTimerRunning$.asObservable();
  }

  get isTimerRunning(): boolean {
    return this._isTimerRunning$.value;
  }

  async checkConnection(): Promise<void> {
    if (this.isChecking) return;
    
    this.isChecking = true;
    this.updateStatus(ConnectionStatus.CONNECTING); // Don't pass errorMessage, preserve existing one

    try {
      const requestBody = {
        siteNumber: environment.siteNumber
      };
      
      const result = await this.http.put(this.connStatusUrl, requestBody).pipe(
        catchError((error: HttpErrorResponse) => {
          console.error('Connection check failed:', error);
          let errorMessage = 'Unknown error occurred';
          
          if (error.error instanceof ErrorEvent) {
            // Client-side error
            errorMessage = `Error: ${error.error.message}`;
          } else {
            // Server-side error
            errorMessage = `Error Code: ${error.status}\nMessage: ${error.message}`;
          }
          
          this.updateStatus(ConnectionStatus.ERROR, errorMessage);
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
    this.timerSubscription = interval(this.checkInterval).pipe(
      switchMap(() => {
        this.checkConnection();
        return of(null);
      })
    ).subscribe();
  }

  startTimer(): void {
    if (!this._isTimerRunning$.value) {
      this._isTimerRunning$.next(true);
      this.startPeriodicCheck();
    }
  }

  stopTimer(): void {
    if (this._isTimerRunning$.value) {
      this._isTimerRunning$.next(false);
      if (this.timerSubscription) {
        this.timerSubscription.unsubscribe();
        this.timerSubscription = undefined;
      }
    }
  }

  toggleTimer(): void {
    if (this._isTimerRunning$.value) {
      this.stopTimer();
    } else {
      this.startTimer();
    }
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
