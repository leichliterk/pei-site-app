import { ServiceConfig } from './websocket-client';
export interface FtpConfig {
    ftpEnabled: boolean;
    ftpHost: string;
    ftpPath: string;
    ftpPollInterval: number;
}
export interface FullConfig extends ServiceConfig, FtpConfig {
}
export declare class ConfigManager {
    private configPath;
    private config;
    constructor();
    private loadConfig;
    private readFromRegistry;
    private readRegistryValue;
    getConfig(): FullConfig;
    getFtpConfig(): FtpConfig;
    updateConfig(updates: Partial<FullConfig>): void;
    private saveConfig;
}
//# sourceMappingURL=config-manager.d.ts.map