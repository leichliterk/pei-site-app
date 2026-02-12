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
exports.FtpWatcher = void 0;
const ftp = __importStar(require("basic-ftp"));
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const stream_1 = require("stream");
class FtpWatcher {
    constructor(config, wsClient, siteId, tenantId, logFn) {
        this.pollTimer = null;
        this.state = { files: {}, lastPoll: null };
        this._lastResult = 'never polled';
        this._filesForwarded = 0;
        this._isPolling = false;
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
    getStatus() {
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
    updateConfig(config) {
        const wasEnabled = this.config.ftpEnabled;
        this.config = config;
        if (config.ftpEnabled && !wasEnabled) {
            this.start();
        }
        else if (!config.ftpEnabled && wasEnabled) {
            this.stop();
        }
        else if (config.ftpEnabled) {
            // Restart with new config
            this.stop();
            this.start();
        }
    }
    start() {
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
    stop() {
        if (this.pollTimer) {
            clearInterval(this.pollTimer);
            this.pollTimer = null;
            this.log('[FtpWatcher] Stopped');
        }
    }
    async poll() {
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
                const isChanged = existing && (existing.size !== file.size ||
                    existing.modifiedAt !== modifiedAt);
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
                    }
                    catch (downloadErr) {
                        this.log(`[FtpWatcher] Error downloading ${file.name}: ${downloadErr.message}`);
                    }
                }
            }
            this.state.lastPoll = new Date().toISOString();
            this.saveState();
            this._lastResult = `OK - ${files.length} files listed, ${newOrChanged} forwarded`;
            this.log(`[FtpWatcher] Poll complete: ${this._lastResult}`);
        }
        catch (err) {
            this._lastResult = `Error: ${err.message}`;
            this.log(`[FtpWatcher] Poll failed: ${this._lastResult}`);
        }
        finally {
            client.close();
            this._isPolling = false;
        }
    }
    async downloadToString(client, filename) {
        const remotePath = this.config.ftpPath.endsWith('/')
            ? `${this.config.ftpPath}${filename}`
            : `${this.config.ftpPath}/${filename}`;
        const chunks = [];
        const writable = new stream_1.Writable({
            write(chunk, _encoding, callback) {
                chunks.push(Buffer.from(chunk));
                callback();
            }
        });
        await client.downloadTo(writable, remotePath);
        return Buffer.concat(chunks).toString('utf8');
    }
    forwardFile(filename, content) {
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
    async testConnection(host, remotePath) {
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
        }
        catch (err) {
            const message = err.message || 'Failed to connect to FTP server';
            this.log(`[FtpWatcher] Test connection failed: ${message}`);
            return { success: false, message };
        }
        finally {
            client.close();
        }
    }
    loadState() {
        try {
            if (fs.existsSync(this.statePath)) {
                const data = fs.readFileSync(this.statePath, 'utf8');
                this.state = JSON.parse(data);
                this.log(`[FtpWatcher] Loaded state: ${Object.keys(this.state.files).length} tracked files`);
            }
        }
        catch (err) {
            this.log(`[FtpWatcher] Could not load state: ${err.message}`);
            this.state = { files: {}, lastPoll: null };
        }
    }
    saveState() {
        try {
            fs.writeFileSync(this.statePath, JSON.stringify(this.state, null, 2), 'utf8');
        }
        catch (err) {
            this.log(`[FtpWatcher] Could not save state: ${err.message}`);
        }
    }
}
exports.FtpWatcher = FtpWatcher;
//# sourceMappingURL=ftp-watcher.js.map