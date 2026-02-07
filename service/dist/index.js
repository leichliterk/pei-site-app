"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
const websocket_client_1 = require("./websocket-client");
const api_server_1 = require("./api-server");
const config_manager_1 = require("./config-manager");
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
// Setup file logging for Windows Service debugging
const logDir = process.env.PROGRAMDATA ? path.join(process.env.PROGRAMDATA, 'PEI Site Service', 'logs') : './logs';
if (!fs.existsSync(logDir)) {
    try {
        fs.mkdirSync(logDir, { recursive: true });
    }
    catch (e) {
        // Ignore if we can't create log directory
    }
}
const logFile = path.join(logDir, `service-${new Date().toISOString().split('T')[0]}.log`);
function log(message) {
    const timestamp = new Date().toISOString();
    const logMessage = `[${timestamp}] ${message}`;
    console.log(logMessage);
    try {
        fs.appendFileSync(logFile, logMessage + '\n');
    }
    catch (e) {
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
const configManager = new config_manager_1.ConfigManager();
const config = configManager.getConfig();
log(`[PEI Site Service] Configuration loaded: apiUrl=${config.apiUrl}, siteId=${config.siteId}, tenantId=${config.tenantId}`);
const wsClient = new websocket_client_1.WebSocketClient(config);
const apiServer = new api_server_1.ApiServer(wsClient);
// Log status changes
wsClient.on('statusChange', (status) => {
    log(`[PEI Site Service] Connection status: ${status}`);
});
// Graceful shutdown
async function shutdown(signal) {
    log(`[PEI Site Service] Received ${signal}, shutting down...`);
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
async function waitForNetwork(maxAttempts = 30, delayMs = 2000) {
    const dns = require('dns').promises;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            log(`[PEI Site Service] Checking network availability (attempt ${attempt}/${maxAttempts})...`);
            await dns.lookup('pei-web-server.onrender.com');
            log('[PEI Site Service] Network is available');
            return true;
        }
        catch (err) {
            log(`[PEI Site Service] Network not ready: ${err.message}`);
            if (attempt < maxAttempts) {
                await new Promise(resolve => setTimeout(resolve, delayMs));
            }
        }
    }
    log('[PEI Site Service] Network check timed out, will continue anyway (socket.io will retry)');
    return false;
}
// Start the service
async function start() {
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
        log('[PEI Site Service] Service started successfully');
    }
    catch (err) {
        log(`[PEI Site Service] Failed to start: ${err.message}`);
        // Don't exit - keep the service running so it can retry
        log('[PEI Site Service] Service will continue running and retry connections');
    }
}
// Keep the process alive
setInterval(() => {
    // Heartbeat to prevent process from exiting
}, 60000);
start();
//# sourceMappingURL=index.js.map