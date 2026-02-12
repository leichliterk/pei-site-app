import { WebSocketClient, ConnectionStatus } from './websocket-client';
import { ApiServer } from './api-server';
import { ConfigManager } from './config-manager';
import { FtpWatcher } from './ftp-watcher';
import * as fs from 'fs';
import * as path from 'path';

// Setup file logging for Windows Service debugging
const logDir = process.env.PROGRAMDATA ? path.join(process.env.PROGRAMDATA, 'PEI Site Service', 'logs') : './logs';
if (!fs.existsSync(logDir)) {
  try {
    fs.mkdirSync(logDir, { recursive: true });
  } catch (e) {
    // Ignore if we can't create log directory
  }
}

const logFile = path.join(logDir, `service-${new Date().toISOString().split('T')[0]}.log`);

function log(message: string): void {
  const timestamp = new Date().toISOString();
  const logMessage = `[${timestamp}] ${message}`;
  console.log(logMessage);
  try {
    fs.appendFileSync(logFile, logMessage + '\n');
  } catch (e) {
    // Ignore file write errors
  }
}

// Catch uncaught exceptions to prevent service from crashing
process.on('uncaughtException', (err) => {
  log(`[FATAL] Uncaught exception: ${err.message}`);
  log(err.stack || '');
});

process.on('unhandledRejection', (reason) => {
  log(`[WARN] Unhandled promise rejection: ${reason}`);
});

log('[PEI Site Service] Starting...');
log(`[PEI Site Service] Process ID: ${process.pid}`);
log(`[PEI Site Service] Working directory: ${process.cwd()}`);
log(`[PEI Site Service] Log file: ${logFile}`);

// Initialize components
const configManager = new ConfigManager();
const config = configManager.getConfig();

log(`[PEI Site Service] Configuration loaded: apiUrl=${config.apiUrl}, siteId=${config.siteId}, tenantId=${config.tenantId}`);

const wsClient = new WebSocketClient(config);
const apiServer = new ApiServer(wsClient);

// Initialize FTP watcher
const ftpConfig = configManager.getFtpConfig();
const ftpWatcher = new FtpWatcher(ftpConfig, wsClient, config.siteId, config.tenantId, log);
apiServer.setFtpWatcher(ftpWatcher);
apiServer.setConfigManager(configManager);

log(`[PEI Site Service] FTP config: enabled=${ftpConfig.ftpEnabled}, host=${ftpConfig.ftpHost}`);

// Log status changes
wsClient.on('statusChange', (status: ConnectionStatus) => {
  log(`[PEI Site Service] Connection status: ${status}`);
});

// Graceful shutdown
async function shutdown(signal: string): Promise<void> {
  log(`[PEI Site Service] Received ${signal}, shutting down...`);

  ftpWatcher.stop();
  wsClient.disconnect();
  await apiServer.stop();

  log('[PEI Site Service] Shutdown complete');
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

// Check if network is available by attempting DNS lookup
async function waitForNetwork(maxAttempts: number = 30, delayMs: number = 2000): Promise<boolean> {
  const dns = require('dns').promises;

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      log(`[PEI Site Service] Checking network availability (attempt ${attempt}/${maxAttempts})...`);
      await dns.lookup('pei-web-server.onrender.com');
      log('[PEI Site Service] Network is available');
      return true;
    } catch (err) {
      log(`[PEI Site Service] Network not ready: ${(err as Error).message}`);
      if (attempt < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, delayMs));
      }
    }
  }

  log('[PEI Site Service] Network check timed out, will continue anyway (socket.io will retry)');
  return false;
}

// Start the service
async function start(): Promise<void> {
  try {
    log('[PEI Site Service] Initializing...');

    // Start local API server first (this is for local IPC, doesn't need network)
    await apiServer.start();
    log('[PEI Site Service] Local API server started');

    // Wait for network to be available before connecting to WebSocket
    await waitForNetwork();

    // Then connect to WebSocket
    log('[PEI Site Service] Initiating WebSocket connection...');
    wsClient.connect();

    // Start FTP watcher if configured
    ftpWatcher.start();

    log('[PEI Site Service] Service started successfully');
  } catch (err) {
    log(`[PEI Site Service] Failed to start: ${(err as Error).message}`);
    // Don't exit - keep the service running so it can retry
    log('[PEI Site Service] Service will continue running and retry connections');
  }
}

// Keep the process alive
setInterval(() => {
  // Heartbeat to prevent process from exiting
}, 60000);

start();
