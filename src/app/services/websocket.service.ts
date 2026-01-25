import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
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
export class WebSocketService {
  private socket: Socket | null = null;
  private status$ = new BehaviorSubject<WebSocketStatus>(WebSocketStatus.DISCONNECTED);
  private messages$ = new BehaviorSubject<any>(null);

  get connectionStatus$(): Observable<WebSocketStatus> {
    return this.status$.asObservable();
  }

  get currentStatus(): WebSocketStatus {
    return this.status$.value;
  }

  get incomingMessages$(): Observable<any> {
    return this.messages$.asObservable();
  }

  connect(): void {
    if (this.socket?.connected) {
      return;
    }

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
      this.status$.next(WebSocketStatus.CONNECTED);
    });

    this.socket.on('disconnect', (reason) => {
      console.log('Socket.io disconnected:', reason);
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
}
