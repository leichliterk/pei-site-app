import { ServiceConfig } from './websocket-client';
export declare class ConfigManager {
    private configPath;
    private config;
    constructor();
    private loadConfig;
    private readFromRegistry;
    private readRegistryValue;
    getConfig(): ServiceConfig;
    updateConfig(updates: Partial<ServiceConfig>): void;
    private saveConfig;
}
//# sourceMappingURL=config-manager.d.ts.map