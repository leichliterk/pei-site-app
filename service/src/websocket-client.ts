import { io, Socket } from 'socket.io-client';
import { EventEmitter } from 'events';

export enum ConnectionStatus {
  CONNECTING = 'connecting',
  CONNECTED = 'connected',
  DISCONNECTED = 'disconnected',
  ERROR = 'error'
}

export interface ServiceConfig {
  apiUrl: string;
  apiKey: string;
  siteId: number;
  tenantId: number;
}

export class WebSocketClient extends EventEmitter {
  private socket: Socket | null = null;
  private config: ServiceConfig;
  private _status: ConnectionStatus = ConnectionStatus.DISCONNECTED;
  private _connectedAt: Date | null = null;
  private _connectionHistory: number[] = new Array(300).fill(0);
  private historyInterval: NodeJS.Timeout | null = null;

  constructor(config: ServiceConfig) {
    super();
    this.config = config;
  }

  get status(): ConnectionStatus {
    return this._status;
  }

  get connectedAt(): Date | null {
    return this._connectedAt;
  }

  get connectionHistory(): number[] {
    return [...this._connectionHistory];
  }

  get currentUptime(): number {
    if (!this._connectedAt || this._status !== ConnectionStatus.CONNECTED) {
      return 0;
    }
    return Date.now() - this._connectedAt.getTime();
  }

  connect(): void {
    if (this.socket?.connected) {
      return;
    }

    this.startHistoryTracking();
    this.setStatus(ConnectionStatus.CONNECTING);

    const url = `${this.config.apiUrl}/desktop`;
    console.log('[WebSocketClient] Connecting to:', url);

    this.socket = io(url, {
      auth: {
        api_key: this.config.apiKey,
        site_id: this.config.siteId,
        tenant_id: this.config.tenantId,
        connection_source: 'service'  // Identify this as a background service connection
      },
      reconnection: true,
      reconnectionAttempts: Infinity, // Keep trying forever for service
      reconnectionDelay: 3000,
      reconnectionDelayMax: 30000,
      transports: ['websocket', 'polling']
    });

    this.socket.on('connect', () => {
      console.log('[WebSocketClient] Connected');
      this._connectedAt = new Date();
      this.setStatus(ConnectionStatus.CONNECTED);
    });

    this.socket.on('disconnect', (reason) => {
      console.log('[WebSocketClient] Disconnected:', reason);
      this._connectedAt = null;
      this.setStatus(ConnectionStatus.DISCONNECTED);
    });

    this.socket.on('connect_error', (error) => {
      console.error('[WebSocketClient] Connection error:', error.message);
      this.setStatus(ConnectionStatus.ERROR);
    });

    this.socket.on('message', (data) => {
      this.emit('message', data);
    });

    this.socket.onAny((eventName, ...args) => {
      console.log('[WebSocketClient] Event:', eventName, args);
      this.emit('event', { eventName, args });
    });
  }

  disconnect(): void {
    this.stopHistoryTracking();
    if (this.socket) {
      this.socket.disconnect();
      this.socket = null;
    }
    this.setStatus(ConnectionStatus.DISCONNECTED);
  }

  updateConfig(config: Partial<ServiceConfig>): void {
    const needsReconnect = this.socket?.connected && (
      config.apiUrl !== undefined ||
      config.apiKey !== undefined ||
      config.siteId !== undefined ||
      config.tenantId !== undefined
    );

    this.config = { ...this.config, ...config };

    if (needsReconnect) {
      console.log('[WebSocketClient] Config changed, reconnecting...');
      this.disconnect();
      this.connect();
    }
  }

  private setStatus(status: ConnectionStatus): void {
    if (this._status !== status) {
      this._status = status;
      this.emit('statusChange', status);
    }
  }

  private startHistoryTracking(): void {
    if (this.historyInterval) {
      return;
    }

    this.historyInterval = setInterval(() => {
      const isConnected = this._status === ConnectionStatus.CONNECTED ? 1 : 0;
      this._connectionHistory.shift();
      this._connectionHistory.push(isConnected);
    }, 1000);
  }

  private stopHistoryTracking(): void {
    if (this.historyInterval) {
      clearInterval(this.historyInterval);
      this.historyInterval = null;
    }
  }

  prepopulateHistory(sessions: Array<{ connected_at: string; disconnected_at: string | null }>): void {
    const now = new Date();
    const fiveMinutesAgo = new Date(now.getTime() - 300 * 1000);

    this._connectionHistory = new Array(300).fill(0);

    for (const session of sessions) {
      const connectedAt = new Date(session.connected_at);
      const disconnectedAt = session.disconnected_at ? new Date(session.disconnected_at) : now;

      if (disconnectedAt < fiveMinutesAgo) {
        continue;
      }

      const sessionStart = Math.max(connectedAt.getTime(), fiveMinutesAgo.getTime());
      const sessionEnd = Math.min(disconnectedAt.getTime(), now.getTime());

      const startIndex = Math.floor((sessionStart - fiveMinutesAgo.getTime()) / 1000);
      const endIndex = Math.floor((sessionEnd - fiveMinutesAgo.getTime()) / 1000);

      for (let i = Math.max(0, startIndex); i <= Math.min(299, endIndex); i++) {
        this._connectionHistory[i] = 1;
      }
    }

    console.log('[WebSocketClient] History prepopulated from', sessions.length, 'sessions');
  }
}
