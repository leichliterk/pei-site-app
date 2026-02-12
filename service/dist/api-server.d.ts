import { WebSocketClient } from './websocket-client';
import { FtpWatcher } from './ftp-watcher';
import { ConfigManager } from './config-manager';
declare const API_PORT = 47836;
export declare class ApiServer {
    private app;
    private server;
    private wsClient;
    private ftpWatcher;
    private configManager;
    constructor(wsClient: WebSocketClient);
    setFtpWatcher(ftpWatcher: FtpWatcher): void;
    setConfigManager(configManager: ConfigManager): void;
    private setupRoutes;
    start(): Promise<void>;
    stop(): Promise<void>;
}
export { API_PORT };
//# sourceMappingURL=api-server.d.ts.map