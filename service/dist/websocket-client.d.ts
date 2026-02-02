import { EventEmitter } from 'events';
export declare enum ConnectionStatus {
    CONNECTING = "connecting",
    CONNECTED = "connected",
    DISCONNECTED = "disconnected",
    ERROR = "error"
}
export interface ServiceConfig {
    apiUrl: string;
    apiKey: string;
    siteId: number;
    tenantId: number;
}
export declare class WebSocketClient extends EventEmitter {
    private socket;
    private config;
    private _status;
    private _connectedAt;
    private _connectionHistory;
    private historyInterval;
    constructor(config: ServiceConfig);
    get status(): ConnectionStatus;
    get connectedAt(): Date | null;
    get connectionHistory(): number[];
    get currentUptime(): number;
    connect(): void;
    disconnect(): void;
    updateConfig(config: Partial<ServiceConfig>): void;
    private setStatus;
    private startHistoryTracking;
    private stopHistoryTracking;
    prepopulateHistory(sessions: Array<{
        connected_at: string;
        disconnected_at: string | null;
    }>): void;
}
//# sourceMappingURL=websocket-client.d.ts.map