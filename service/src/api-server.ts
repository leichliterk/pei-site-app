import express, { Request, Response } from 'express';
import { WebSocketClient, ConnectionStatus, ServiceConfig } from './websocket-client';
import { FtpWatcher } from './ftp-watcher';
import { ConfigManager, FtpConfig } from './config-manager';

const API_PORT = 47836; // Local API port for UI communication

export class ApiServer {
  private app = express();
  private server: ReturnType<typeof this.app.listen> | null = null;
  private wsClient: WebSocketClient;
  private ftpWatcher: FtpWatcher | null = null;
  private configManager: ConfigManager | null = null;

  constructor(wsClient: WebSocketClient) {
    this.wsClient = wsClient;
    this.setupRoutes();
  }

  setFtpWatcher(ftpWatcher: FtpWatcher): void {
    this.ftpWatcher = ftpWatcher;
  }

  setConfigManager(configManager: ConfigManager): void {
    this.configManager = configManager;
  }

  private setupRoutes(): void {
    this.app.use(express.json());

    // CORS for local Electron app
    this.app.use((_req, res, next) => {
      res.header('Access-Control-Allow-Origin', '*');
      res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, OPTIONS');
      res.header('Access-Control-Allow-Headers', 'Content-Type');
      next();
    });

    // Health check
    this.app.get('/health', (_req: Request, res: Response) => {
      res.json({ status: 'ok', timestamp: new Date().toISOString() });
    });

    // Get current connection status
    this.app.get('/status', (_req: Request, res: Response) => {
      res.json({
        status: this.wsClient.status,
        connectedAt: this.wsClient.connectedAt?.toISOString() || null,
        currentUptime: this.wsClient.currentUptime,
        connectionHistory: this.wsClient.connectionHistory
      });
    });

    // Get connection history (for chart)
    this.app.get('/history', (_req: Request, res: Response) => {
      res.json({
        history: this.wsClient.connectionHistory,
        timestamp: new Date().toISOString()
      });
    });

    // Update configuration (from UI settings)
    this.app.post('/config', (req: Request, res: Response) => {
      const config: Partial<ServiceConfig> = req.body;
      console.log('[ApiServer] Updating config:', config);
      this.wsClient.updateConfig(config);
      res.json({ success: true });
    });

    // Force reconnect
    this.app.post('/reconnect', (_req: Request, res: Response) => {
      console.log('[ApiServer] Force reconnect requested');
      this.wsClient.disconnect();
      this.wsClient.connect();
      res.json({ success: true });
    });

    // Prepopulate history from uptime data
    this.app.post('/prepopulate-history', (req: Request, res: Response) => {
      const { sessions } = req.body;
      if (Array.isArray(sessions)) {
        this.wsClient.prepopulateHistory(sessions);
        res.json({ success: true });
      } else {
        res.status(400).json({ error: 'sessions array required' });
      }
    });

    // FTP watcher status
    this.app.get('/ftp/status', (_req: Request, res: Response) => {
      if (!this.ftpWatcher) {
        res.json({ enabled: false, lastResult: 'FTP watcher not initialized' });
        return;
      }
      res.json(this.ftpWatcher.getStatus());
    });

    // Update FTP config
    this.app.post('/ftp/config', (req: Request, res: Response) => {
      const ftpConfig: Partial<FtpConfig> = req.body;
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
    this.app.post('/ftp/test', async (req: Request, res: Response) => {
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
    this.app.post('/ftp/poll', async (_req: Request, res: Response) => {
      if (!this.ftpWatcher) {
        res.status(500).json({ error: 'FTP watcher not initialized' });
        return;
      }
      console.log('[ApiServer] Manual FTP poll triggered');
      await this.ftpWatcher.poll();
      res.json(this.ftpWatcher.getStatus());
    });
  }

  start(): Promise<void> {
    return new Promise((resolve, reject) => {
      try {
        this.server = this.app.listen(API_PORT, '127.0.0.1', () => {
          console.log(`[ApiServer] Local API server running on http://127.0.0.1:${API_PORT}`);
          resolve();
        });

        this.server.on('error', (err: NodeJS.ErrnoException) => {
          if (err.code === 'EADDRINUSE') {
            console.error(`[ApiServer] Port ${API_PORT} is already in use. Service may already be running.`);
          }
          reject(err);
        });
      } catch (err) {
        reject(err);
      }
    });
  }

  stop(): Promise<void> {
    return new Promise((resolve) => {
      if (this.server) {
        this.server.close(() => {
          console.log('[ApiServer] Server stopped');
          resolve();
        });
      } else {
        resolve();
      }
    });
  }
}

export { API_PORT };
