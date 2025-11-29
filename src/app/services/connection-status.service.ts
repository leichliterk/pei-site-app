import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, interval, catchError, of, switchMap, Subscription, firstValueFrom } from 'rxjs';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { ElectronService } from './electron.service';

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

interface QueuedFailure {
  timestamp: string;
  details: string;
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
  private readonly healthCheckUrl = `${environment.apiUrl}/log-connection`;
  private timerSubscription?: Subscription;
  private _isTimerRunning$ = new BehaviorSubject<boolean>(false);
  private readonly QUEUE_STORAGE_KEY = 'connection_failure_queue';
  private failureQueue: QueuedFailure[] = [];
  private siteNumber: number = environment.siteNumber;

  constructor(
    private http: HttpClient,
    private electronService: ElectronService
  ) {
    this.loadFailureQueue();
    this.loadSiteNumber();
    this.loadTimerState();
  }

  /**
   * Load site number from Electron persistent storage
   */
  private async loadSiteNumber(): Promise<void> {
    try {
      const savedSiteNumber = await this.electronService.getSiteNumber();
      if (savedSiteNumber !== null) {
        this.siteNumber = savedSiteNumber;
      }
    } catch (error) {
      console.error('Failed to load site number from persistent storage:', error);
      // Fall back to environment default
      this.siteNumber = environment.siteNumber;
    }
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

  /**
   * Load failure queue from localStorage
   */
  private loadFailureQueue(): void {
    try {
      const stored = localStorage.getItem(this.QUEUE_STORAGE_KEY);
      if (stored) {
        this.failureQueue = JSON.parse(stored);
      }
    } catch (error) {
      console.error('Failed to load failure queue from localStorage:', error);
      this.failureQueue = [];
    }
  }

  /**
   * Save failure queue to localStorage
   */
  private saveFailureQueue(): void {
    try {
      localStorage.setItem(this.QUEUE_STORAGE_KEY, JSON.stringify(this.failureQueue));
    } catch (error) {
      console.error('Failed to save failure queue to localStorage:', error);
    }
  }

  /**
   * Add a failure to the queue
   */
  private queueFailure(details: string): void {
    const failure: QueuedFailure = {
      timestamp: new Date().toISOString(),
      details
    };
    this.failureQueue.push(failure);
    this.saveFailureQueue();
  }

  /**
   * Clear the failure queue
   */
  private clearFailureQueue(): void {
    this.failureQueue = [];
    this.saveFailureQueue();
  }

  /**
   * Load timer state from localStorage
   */
  private loadTimerState(): void {
    try {
      const stored = localStorage.getItem('connection_timer_enabled');
      const isEnabled = stored === 'true';
      this._isTimerRunning$.next(isEnabled);

      if (isEnabled) {
        this.startPeriodicCheck();
      }
    } catch (error) {
      console.error('Failed to load timer state:', error);
    }
  }

  /**
   * Save timer state to localStorage
   */
  private saveTimerState(enabled: boolean): void {
    try {
      localStorage.setItem('connection_timer_enabled', enabled.toString());
    } catch (error) {
      console.error('Failed to save timer state:', error);
    }
  }

  /**
   * Perform a connection check
   */
  async checkConnection(): Promise<void> {
    if (this.isChecking) return;

    this.isChecking = true;
    this.updateStatus(ConnectionStatus.CONNECTING);

    try {
      const requestBody: any = {
        site_id: this.siteNumber,
        status: 'success',
        timestamp: new Date().toISOString(),
        details: 'Connection check successful'
      };

      // Include queued failures if any exist
      if (this.failureQueue.length > 0) {
        requestBody.queuedFailures = [...this.failureQueue];
      }

      const result = await firstValueFrom(
        this.http.post(this.healthCheckUrl, requestBody).pipe(
          catchError((error: HttpErrorResponse) => {
            console.error('Connection check failed:', error);
            let errorMessage = 'Unknown error occurred';

            if (error.error instanceof ErrorEvent) {
              // Client-side error
              errorMessage = `Client Error: ${error.error.message}`;
            } else if (error.status === 0) {
              // Network error
              errorMessage = 'Network error: Unable to reach server';
            } else if (error.status === 413) {
              // Payload too large - clear the queue to prevent infinite loop
              console.warn('Payload too large (413). Clearing failure queue to prevent accumulation.');
              this.clearFailureQueue();
              errorMessage = 'Payload too large: Previous failure queue cleared';
            } else {
              // Server-side error
              errorMessage = `Server Error ${error.status}: ${error.message}`;
            }

            // Queue this failure (unless it was a 413 error where we just cleared the queue)
            if (error.status !== 413) {
              this.queueFailure(errorMessage);
            }

            this.updateStatus(ConnectionStatus.ERROR, errorMessage);
            this.isChecking = false;
            return of(null);
          })
        )
      );

      // Success - log was recorded and queued failures were sent
      if (result !== null) {
        // Clear the queue since failures were successfully logged
        this.clearFailureQueue();
        this.updateStatus(ConnectionStatus.CONNECTED);
      }
    } catch (error: any) {
      const errorMessage = error?.message || 'Unexpected error occurred';
      this.queueFailure(errorMessage);
      this.updateStatus(ConnectionStatus.ERROR, errorMessage);
    } finally {
      this.isChecking = false;
    }
  }

  /**
   * Start periodic connection checks
   */
  private startPeriodicCheck(): void {
    // Stop any existing timer
    if (this.timerSubscription) {
      this.timerSubscription.unsubscribe();
    }

    // Initial check
    this.checkConnection();

    // Set up periodic checks every 15 seconds
    this.timerSubscription = interval(this.checkInterval).pipe(
      switchMap(() => {
        this.checkConnection();
        return of(null);
      })
    ).subscribe();
  }

  /**
   * Start the connection timer
   */
  startTimer(): void {
    if (!this._isTimerRunning$.value) {
      this._isTimerRunning$.next(true);
      this.saveTimerState(true);
      this.startPeriodicCheck();
    }
  }

  /**
   * Stop the connection timer
   */
  stopTimer(): void {
    if (this._isTimerRunning$.value) {
      this._isTimerRunning$.next(false);
      this.saveTimerState(false);
      if (this.timerSubscription) {
        this.timerSubscription.unsubscribe();
        this.timerSubscription = undefined;
      }
      // Update status to disconnected when timer stops
      this.updateStatus(ConnectionStatus.DISCONNECTED);
    }
  }

  /**
   * Toggle the connection timer on/off
   */
  toggleTimer(): void {
    if (this._isTimerRunning$.value) {
      this.stopTimer();
    } else {
      this.startTimer();
    }
  }

  /**
   * Update the connection status
   */
  private updateStatus(status: ConnectionStatus, errorMessage?: string): void {
    const currentState = this.connectionState$.value;
    const newState: ConnectionState = {
      status,
      lastChecked: new Date(),
      errorMessage: errorMessage !== undefined ? errorMessage : currentState.errorMessage
    };

    // Clear error message when connecting successfully
    if (status === ConnectionStatus.CONNECTED) {
      newState.errorMessage = undefined;
    }

    this.connectionState$.next(newState);
  }

  /**
   * Manually retry connection
   */
  retryConnection(): void {
    if (this._isTimerRunning$.value) {
      this.checkConnection();
    }
  }

  /**
   * Get the current failure queue (for debugging/monitoring)
   */
  getFailureQueue(): QueuedFailure[] {
    return [...this.failureQueue];
  }

  /**
   * Get the failure queue size
   */
  getFailureQueueSize(): number {
    return this.failureQueue.length;
  }

  /**
   * Get connection logs from the server
   */
  async getConnectionLogs(): Promise<ConnectionLog[]> {
    // Ensure we have the latest site number from storage
    const siteNumber = await this.electronService.getSiteNumber() ?? this.siteNumber;
    const url = `${environment.apiUrl}/site/getConnectionLogs/${siteNumber}?limit=250`;
    try {
      const result = await firstValueFrom(
        this.http.get<ConnectionLogsResponse>(url).pipe(
          catchError((error: HttpErrorResponse) => {
            console.error('Failed to fetch connection logs:', error);
            return of({ logs: [], count: 0, site_id: siteNumber, limit: 0 });
          })
        )
      );
      return result.logs;
    } catch (error) {
      console.error('Failed to fetch connection logs:', error);
      return [];
    }
  }
}

export interface ConnectionLogsResponse {
  site_id: number;
  limit: number;
  count: number;
  logs: ConnectionLog[];
}

export interface ConnectionLog {
  _id: string;
  site_id: number;
  status: string;
  timestamp: string;
  details?: string;
}
