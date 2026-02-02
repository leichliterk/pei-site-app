import express, { Request, Response } from 'express';
import { WebSocketClient, ConnectionStatus, ServiceConfig } from './websocket-client';

const API_PORT = 47836; // Local API port for UI communication

export class ApiServer {
  private app = express();
  private server: ReturnType<typeof this.app.listen> | null = null;
  private wsClient: WebSocketClient;

  constructor(wsClient: WebSocketClient) {
    this.wsClient = wsClient;
    this.setupRoutes();
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
