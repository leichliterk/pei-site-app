import { WebSocketClient } from './websocket-client';
declare const API_PORT = 47836;
export declare class ApiServer {
    private app;
    private server;
    private wsClient;
    constructor(wsClient: WebSocketClient);
    private setupRoutes;
    start(): Promise<void>;
    stop(): Promise<void>;
}
export { API_PORT };
//# sourceMappingURL=api-server.d.ts.map