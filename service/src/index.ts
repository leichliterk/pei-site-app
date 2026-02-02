import { WebSocketClient, ConnectionStatus } from './websocket-client';
import { ApiServer } from './api-server';
import { ConfigManager } from './config-manager';

console.log('[PEI Site Service] Starting...');
console.log('[PEI Site Service] Process ID:', process.pid);
console.log('[PEI Site Service] Working directory:', process.cwd());

// Initialize components
const configManager = new ConfigManager();
const config = configManager.getConfig();

console.log('[PEI Site Service] Configuration loaded:', {
  apiUrl: config.apiUrl,
  siteId: config.siteId,
  tenantId: config.tenantId
});

const wsClient = new WebSocketClient(config);
const apiServer = new ApiServer(wsClient);

// Log status changes
wsClient.on('statusChange', (status: ConnectionStatus) => {
  console.log('[PEI Site Service] Connection status:', status);
});

// Graceful shutdown
async function shutdown(signal: string): Promise<void> {
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
async function start(): Promise<void> {
  try {
    // Start local API server first
    await apiServer.start();

    // Then connect to WebSocket
    wsClient.connect();

    console.log('[PEI Site Service] Service started successfully');
  } catch (err) {
    console.error('[PEI Site Service] Failed to start:', err);
    process.exit(1);
  }
}

start();
