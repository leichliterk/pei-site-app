import * as ftp from 'basic-ftp';
import * as fs from 'fs';
import * as path from 'path';
import { Writable } from 'stream';
import { WebSocketClient } from './websocket-client';
import { FtpConfig } from './config-manager';

interface FileState {
  name: string;
  size: number;
  modifiedAt: string;
}

interface FtpState {
  files: Record<string, FileState>;
  lastPoll: string | null;
}

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

export class FtpWatcher {
  private config: FtpConfig;
  private wsClient: WebSocketClient;
  private siteId: number;
  private tenantId: number;
  private log: (message: string) => void;

  private pollTimer: NodeJS.Timeout | null = null;
  private state: FtpState = { files: {}, lastPoll: null };
  private statePath: string;
  private _lastResult: string = 'never polled';
  private _filesForwarded: number = 0;
  private _isPolling: boolean = false;

  constructor(
    config: FtpConfig,
    wsClient: WebSocketClient,
    siteId: number,
    tenantId: number,
    logFn: (message: string) => void
  ) {
    this.config = config;
    this.wsClient = wsClient;
    this.siteId = siteId;
    this.tenantId = tenantId;
    this.log = logFn;

    const programData = process.env.PROGRAMDATA || 'C:\\ProgramData';
    const stateDir = path.join(programData, 'PEI Site Service');
    if (!fs.existsSync(stateDir)) {
      fs.mkdirSync(stateDir, { recursive: true });
    }
    this.statePath = path.join(stateDir, 'ftp-state.json');
    this.loadState();
  }

  getStatus(): FtpWatcherStatus {
    return {
      enabled: this.config.ftpEnabled,
      host: this.config.ftpHost,
      path: this.config.ftpPath,
      pollInterval: this.config.ftpPollInterval,
      lastPoll: this.state.lastPoll,
      lastResult: this._lastResult,
      filesForwarded: this._filesForwarded,
      isPolling: this._isPolling
    };
  }

  updateConfig(config: FtpConfig): void {
    const wasEnabled = this.config.ftpEnabled;
    this.config = config;

    if (config.ftpEnabled && !wasEnabled) {
      this.start();
    } else if (!config.ftpEnabled && wasEnabled) {
      this.stop();
    } else if (config.ftpEnabled) {
      // Restart with new config
      this.stop();
      this.start();
    }
  }

  start(): void {
    if (!this.config.ftpEnabled || !this.config.ftpHost) {
      this.log('[FtpWatcher] Not starting - FTP is disabled or no host configured');
      return;
    }

    if (this.pollTimer) {
      this.log('[FtpWatcher] Already running');
      return;
    }

    this.log(`[FtpWatcher] Starting - host: ${this.config.ftpHost}, path: ${this.config.ftpPath}, interval: ${this.config.ftpPollInterval}s`);

    // Poll immediately, then on interval
    this.poll();
    this.pollTimer = setInterval(() => this.poll(), this.config.ftpPollInterval * 1000);
  }

  stop(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
      this.log('[FtpWatcher] Stopped');
    }
  }

  async poll(): Promise<void> {
    if (this._isPolling) {
      this.log('[FtpWatcher] Poll already in progress, skipping');
      return;
    }

    this._isPolling = true;
    const client = new ftp.Client();

    try {
      this.log(`[FtpWatcher] Connecting to ${this.config.ftpHost}...`);
      await client.access({
        host: this.config.ftpHost,
        user: 'anonymous',
        password: 'anonymous@',
        secure: false
      });

      this.log(`[FtpWatcher] Listing ${this.config.ftpPath}`);
      const fileList = await client.list(this.config.ftpPath);

      // Filter to only files (not directories)
      const files = fileList.filter(f => f.type === ftp.FileType.File);
      let newOrChanged = 0;

      for (const file of files) {
        const key = file.name;
        const modifiedAt = file.modifiedAt ? file.modifiedAt.toISOString() : '';
        const existing = this.state.files[key];

        const isNew = !existing;
        const isChanged = existing && (
          existing.size !== file.size ||
          existing.modifiedAt !== modifiedAt
        );

        if (isNew || isChanged) {
          this.log(`[FtpWatcher] ${isNew ? 'New' : 'Changed'} file: ${file.name} (${file.size} bytes)`);

          try {
            const content = await this.downloadToString(client, file.name);
            this.forwardFile(file.name, content);
            newOrChanged++;

            // Update state for this file
            this.state.files[key] = {
              name: file.name,
              size: file.size,
              modifiedAt
            };
          } catch (downloadErr) {
            this.log(`[FtpWatcher] Error downloading ${file.name}: ${(downloadErr as Error).message}`);
          }
        }
      }

      this.state.lastPoll = new Date().toISOString();
      this.saveState();

      this._lastResult = `OK - ${files.length} files listed, ${newOrChanged} forwarded`;
      this.log(`[FtpWatcher] Poll complete: ${this._lastResult}`);
    } catch (err) {
      this._lastResult = `Error: ${(err as Error).message}`;
      this.log(`[FtpWatcher] Poll failed: ${this._lastResult}`);
    } finally {
      client.close();
      this._isPolling = false;
    }
  }

  private async downloadToString(client: ftp.Client, filename: string): Promise<string> {
    const remotePath = this.config.ftpPath.endsWith('/')
      ? `${this.config.ftpPath}${filename}`
      : `${this.config.ftpPath}/${filename}`;

    const chunks: Buffer[] = [];
    const writable = new Writable({
      write(chunk, _encoding, callback) {
        chunks.push(Buffer.from(chunk));
        callback();
      }
    });

    await client.downloadTo(writable, remotePath);
    return Buffer.concat(chunks).toString('utf8');
  }

  private forwardFile(filename: string, content: string): void {
    const payload = {
      filename,
      content,
      siteId: this.siteId,
      tenantId: this.tenantId,
      timestamp: new Date().toISOString()
    };

    this.wsClient.emitToServer('ftp:file', payload);
    this._filesForwarded++;
    this.log(`[FtpWatcher] Forwarded: ${filename} (${content.length} chars)`);
  }

  async testConnection(host: string, remotePath: string): Promise<{ success: boolean; message: string; fileCount?: number }> {
    const client = new ftp.Client();

    try {
      this.log(`[FtpWatcher] Testing connection to ${host}${remotePath}...`);
      await client.access({
        host,
        user: 'anonymous',
        password: 'anonymous@',
        secure: false
      });

      await client.cd(remotePath);
      const list = await client.list();

      this.log(`[FtpWatcher] Test connection successful: ${list.length} items found`);
      return {
        success: true,
        message: `Connected successfully. Found ${list.length} items in ${remotePath}`,
        fileCount: list.length
      };
    } catch (err) {
      const message = (err as Error).message || 'Failed to connect to FTP server';
      this.log(`[FtpWatcher] Test connection failed: ${message}`);
      return { success: false, message };
    } finally {
      client.close();
    }
  }

  private loadState(): void {
    try {
      if (fs.existsSync(this.statePath)) {
        const data = fs.readFileSync(this.statePath, 'utf8');
        this.state = JSON.parse(data);
        this.log(`[FtpWatcher] Loaded state: ${Object.keys(this.state.files).length} tracked files`);
      }
    } catch (err) {
      this.log(`[FtpWatcher] Could not load state: ${(err as Error).message}`);
      this.state = { files: {}, lastPoll: null };
    }
  }

  private saveState(): void {
    try {
      fs.writeFileSync(this.statePath, JSON.stringify(this.state, null, 2), 'utf8');
    } catch (err) {
      this.log(`[FtpWatcher] Could not save state: ${(err as Error).message}`);
    }
  }
}
