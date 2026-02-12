import { WebSocketClient } from './websocket-client';
import { FtpConfig } from './config-manager';
export interface FtpWatcherStatus {
    enabled: boolean;
    host: string;
    path: string;
    pollInterval: number;
    lastPoll: string | null;
    lastResult: string;
    filesForwarded: number;
    isPolling: boolean;
}
export declare class FtpWatcher {
    private config;
    private wsClient;
    private siteId;
    private tenantId;
    private log;
    private pollTimer;
    private state;
    private statePath;
    private _lastResult;
    private _filesForwarded;
    private _isPolling;
    constructor(config: FtpConfig, wsClient: WebSocketClient, siteId: number, tenantId: number, logFn: (message: string) => void);
    getStatus(): FtpWatcherStatus;
    updateConfig(config: FtpConfig): void;
    start(): void;
    stop(): void;
    poll(): Promise<void>;
    private downloadToString;
    private forwardFile;
    testConnection(host: string, remotePath: string): Promise<{
        success: boolean;
        message: string;
        fileCount?: number;
    }>;
    private loadState;
    private saveState;
}
//# sourceMappingURL=ftp-watcher.d.ts.map