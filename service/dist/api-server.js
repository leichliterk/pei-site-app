"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.API_PORT = exports.ApiServer = void 0;
const express_1 = __importDefault(require("express"));
const API_PORT = 47836; // Local API port for UI communication
exports.API_PORT = API_PORT;
class ApiServer {
    constructor(wsClient) {
        this.app = (0, express_1.default)();
        this.server = null;
        this.ftpWatcher = null;
        this.configManager = null;
        this.wsClient = wsClient;
        this.setupRoutes();
    }
    setFtpWatcher(ftpWatcher) {
        this.ftpWatcher = ftpWatcher;
    }
    setConfigManager(configManager) {
        this.configManager = configManager;
    }
    setupRoutes() {
        this.app.use(express_1.default.json());
        // CORS for local Electron app
        this.app.use((_req, res, next) => {
            res.header('Access-Control-Allow-Origin', '*');
            res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, OPTIONS');
            res.header('Access-Control-Allow-Headers', 'Content-Type');
            next();
        });
        // Health check
        this.app.get('/health', (_req, res) => {
            res.json({ status: 'ok', timestamp: new Date().toISOString() });
        });
        // Get current connection status
        this.app.get('/status', (_req, res) => {
            res.json({
                status: this.wsClient.status,
                connectedAt: this.wsClient.connectedAt?.toISOString() || null,
                currentUptime: this.wsClient.currentUptime,
                connectionHistory: this.wsClient.connectionHistory
            });
        });
        // Get connection history (for chart)
        this.app.get('/history', (_req, res) => {
            res.json({
                history: this.wsClient.connectionHistory,
                timestamp: new Date().toISOString()
            });
        });
        // Update configuration (from UI settings)
        this.app.post('/config', (req, res) => {
            const config = req.body;
            console.log('[ApiServer] Updating config:', config);
            this.wsClient.updateConfig(config);
            res.json({ success: true });
        });
        // Force reconnect
        this.app.post('/reconnect', (_req, res) => {
            console.log('[ApiServer] Force reconnect requested');
            this.wsClient.disconnect();
            this.wsClient.connect();
            res.json({ success: true });
        });
        // Prepopulate history from uptime data
        this.app.post('/prepopulate-history', (req, res) => {
            const { sessions } = req.body;
            if (Array.isArray(sessions)) {
                this.wsClient.prepopulateHistory(sessions);
                res.json({ success: true });
            }
            else {
                res.status(400).json({ error: 'sessions array required' });
            }
        });
        // FTP watcher status
        this.app.get('/ftp/status', (_req, res) => {
            if (!this.ftpWatcher) {
                res.json({ enabled: false, lastResult: 'FTP watcher not initialized' });
                return;
            }
            res.json(this.ftpWatcher.getStatus());
        });
        // Update FTP config
        this.app.post('/ftp/config', (req, res) => {
            const ftpConfig = req.body;
            if (!this.configManager || !this.ftpWatcher) {
                res.status(500).json({ error: 'FTP watcher not initialized' });
                return;
            }
            console.log('[ApiServer] Updating FTP config:', ftpConfig);
            this.configManager.updateConfig(ftpConfig);
            this.ftpWatcher.updateConfig(this.configManager.getFtpConfig());
            res.json({ success: true });
        });
        // Test FTP connection (lightweight - connect + list only)
        this.app.post('/ftp/test', async (req, res) => {
            if (!this.ftpWatcher) {
                res.status(500).json({ success: false, message: 'FTP watcher not initialized' });
                return;
            }
            const { host, path: remotePath } = req.body;
            if (!host) {
                res.status(400).json({ success: false, message: 'host is required' });
                return;
            }
            console.log('[ApiServer] FTP test connection:', host, remotePath);
            const result = await this.ftpWatcher.testConnection(host, remotePath || '/');
            res.json(result);
        });
        // Trigger immediate FTP poll
        this.app.post('/ftp/poll', async (_req, res) => {
            if (!this.ftpWatcher) {
                res.status(500).json({ error: 'FTP watcher not initialized' });
                return;
            }
            console.log('[ApiServer] Manual FTP poll triggered');
            await this.ftpWatcher.poll();
            res.json(this.ftpWatcher.getStatus());
        });
    }
    start() {
        return new Promise((resolve, reject) => {
            try {
                this.server = this.app.listen(API_PORT, '127.0.0.1', () => {
                    console.log(`[ApiServer] Local API server running on http://127.0.0.1:${API_PORT}`);
                    resolve();
                });
                this.server.on('error', (err) => {
                    if (err.code === 'EADDRINUSE') {
                        console.error(`[ApiServer] Port ${API_PORT} is already in use. Service may already be running.`);
                    }
                    reject(err);
                });
            }
            catch (err) {
                reject(err);
            }
        });
    }
    stop() {
        return new Promise((resolve) => {
            if (this.server) {
                this.server.close(() => {
                    console.log('[ApiServer] Server stopped');
                    resolve();
                });
            }
            else {
                resolve();
            }
        });
    }
}
exports.ApiServer = ApiServer;
//# sourceMappingURL=api-server.js.map