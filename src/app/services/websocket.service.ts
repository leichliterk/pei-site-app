import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable, interval, Subscription } from 'rxjs';
import { io, Socket } from 'socket.io-client';
import { environment } from '../../environments/environment';

export enum WebSocketStatus {
  CONNECTING = 'connecting',
  CONNECTED = 'connected',
  DISCONNECTED = 'disconnected',
  ERROR = 'error'
}

@Injectable({
  providedIn: 'root'
})
export class WebSocketService implements OnDestroy {
  private socket: Socket | null = null;
  private status$ = new BehaviorSubject<WebSocketStatus>(WebSocketStatus.DISCONNECTED);
  private messages$ = new BehaviorSubject<any>(null);
  private _connectedAt: Date | null = null;

  // Connection history (last 5 minutes / 300 seconds)
  private _connectionHistory: number[] = new Array(300).fill(0);
  private historyInterval$: Subscription | null = null;

  get connectionStatus$(): Observable<WebSocketStatus> {
    return this.status$.asObservable();
  }

  get currentStatus(): WebSocketStatus {
    return this.status$.value;
  }

  get connectedAt(): Date | null {
    return this._connectedAt;
  }

  get connectionHistory(): number[] {
    return this._connectionHistory;
  }

  get incomingMessages$(): Observable<any> {
    return this.messages$.asObservable();
  }

  connect(): void {
    if (this.socket?.connected) {
      return;
    }

    // Start tracking connection history
    this.startHistoryTracking();

    this.status$.next(WebSocketStatus.CONNECTING);

    const url = this.buildSocketUrl();
    console.log('Socket.io connecting to:', url);

    this.socket = io(url, {
      auth: {
        api_key: environment.apiKey,
        site_id: environment.siteNumber,
        tenant_id: environment.tenantId,
        connection_source: 'app'  // Identify this as a direct app connection (fallback when service not running)
      },
      reconnection: true,
      reconnectionAttempts: 5,
      reconnectionDelay: 3000,
      transports: ['websocket', 'polling']
    });

    this.socket.on('connect', () => {
      console.log('Socket.io connected');
      this._connectedAt = new Date();
      this.status$.next(WebSocketStatus.CONNECTED);
    });

    this.socket.on('disconnect', (reason) => {
      console.log('Socket.io disconnected:', reason);
      this._connectedAt = null;
      this.status$.next(WebSocketStatus.DISCONNECTED);
    });

    this.socket.on('connect_error', (error) => {
      console.error('Socket.io connection error:', error);
      this.status$.next(WebSocketStatus.ERROR);
    });

    this.socket.on('message', (data) => {
      this.messages$.next(data);
    });

    this.socket.onAny((eventName, ...args) => {
      console.log('Socket.io event:', eventName, args);
    });
  }

  disconnect(): void {
    if (this.socket) {
      this.socket.disconnect();
      this.socket = null;
    }
    this.status$.next(WebSocketStatus.DISCONNECTED);
  }

  send(event: string, data?: any): void {
    if (this.socket?.connected) {
      this.socket.emit(event, data);
    } else {
      console.warn('Socket.io is not connected. Cannot send message.');
    }
  }

  on(event: string, callback: (...args: any[]) => void): void {
    this.socket?.on(event, callback);
  }

  off(event: string, callback?: (...args: any[]) => void): void {
    this.socket?.off(event, callback);
  }

  private buildSocketUrl(): string {
    const apiUrl = environment.apiUrl;
    return `${apiUrl}/desktop`;
  }

  private startHistoryTracking(): void {
    if (this.historyInterval$) {
      return; // Already tracking
    }

    this.historyInterval$ = interval(1000).subscribe(() => {
      const isConnected = this.status$.value === WebSocketStatus.CONNECTED ? 1 : 0;
      this._connectionHistory.shift();
      this._connectionHistory.push(isConnected);
    });
  }

  /**
   * Prepopulate connection history from uptime session data
   * This fills in the last 5 minutes of history based on actual connection data
   */
  prepopulateHistory(sessions: Array<{ connected_at: string; disconnected_at: string | null }>): void {
    const now = new Date();
    const fiveMinutesAgo = new Date(now.getTime() - 300 * 1000);

    // Reset history to all zeros first
    this._connectionHistory = new Array(300).fill(0);

    // For each session, mark the connected seconds in our history
    for (const session of sessions) {
      const connectedAt = new Date(session.connected_at);
      // If disconnected_at is null or empty, the session is still active (use current time)
      const disconnectedAt = session.disconnected_at ? new Date(session.disconnected_at) : now;

      // Skip sessions that ended before our 5-minute window
      if (disconnectedAt < fiveMinutesAgo) {
        continue;
      }

      // Calculate the start and end indices in our history array
      // Index 0 = 5 minutes ago, Index 299 = now
      const sessionStart = Math.max(connectedAt.getTime(), fiveMinutesAgo.getTime());
      const sessionEnd = Math.min(disconnectedAt.getTime(), now.getTime());

      const startIndex = Math.floor((sessionStart - fiveMinutesAgo.getTime()) / 1000);
      const endIndex = Math.floor((sessionEnd - fiveMinutesAgo.getTime()) / 1000);

      // Mark all seconds in this range as connected
      for (let i = Math.max(0, startIndex); i <= Math.min(299, endIndex); i++) {
        this._connectionHistory[i] = 1;
      }
    }

    console.log('Connection history prepopulated from', sessions.length, 'sessions');
  }

  ngOnDestroy(): void {
    this.historyInterval$?.unsubscribe();
    this.disconnect();
  }
}
