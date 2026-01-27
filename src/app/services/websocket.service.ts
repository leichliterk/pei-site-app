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
        tenant_id: environment.tenantId
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

  ngOnDestroy(): void {
    this.historyInterval$?.unsubscribe();
    this.disconnect();
  }
}
