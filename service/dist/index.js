"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const websocket_client_1 = require("./websocket-client");
const api_server_1 = require("./api-server");
const config_manager_1 = require("./config-manager");
console.log('[PEI Site Service] Starting...');
console.log('[PEI Site Service] Process ID:', process.pid);
console.log('[PEI Site Service] Working directory:', process.cwd());
// Initialize components
const configManager = new config_manager_1.ConfigManager();
const config = configManager.getConfig();
console.log('[PEI Site Service] Configuration loaded:', {
    apiUrl: config.apiUrl,
    siteId: config.siteId,
    tenantId: config.tenantId
});
const wsClient = new websocket_client_1.WebSocketClient(config);
const apiServer = new api_server_1.ApiServer(wsClient);
// Log status changes
wsClient.on('statusChange', (status) => {
    console.log('[PEI Site Service] Connection status:', status);
});
// Graceful shutdown
async function shutdown(signal) {
    console.log(`[PEI Site Service] Received ${signal}, shutting down...`);
    wsClient.disconnect();
    await apiServer.stop();
    console.log('[PEI Site Service] Shutdown complete');
    process.exit(0);
}
process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT', () => shutdown('SIGINT'));
// Windows service stop signal
process.on('message', (msg) => {
    if (msg === 'shutdown') {
        shutdown('service-stop');
    }
});
// Start the service
async function start() {
    try {
        // Start local API server first
        await apiServer.start();
        // Then connect to WebSocket
        wsClient.connect();
        console.log('[PEI Site Service] Service started successfully');
    }
    catch (err) {
        console.error('[PEI Site Service] Failed to start:', err);
        process.exit(1);
    }
}
start();
//# sourceMappingURL=index.js.map