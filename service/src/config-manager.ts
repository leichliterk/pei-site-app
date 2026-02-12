import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';
import { ServiceConfig } from './websocket-client';

export interface FtpConfig {
  ftpEnabled: boolean;
  ftpHost: string;
  ftpPath: string;
  ftpPollInterval: number; // seconds
}

export interface FullConfig extends ServiceConfig, FtpConfig {}

// Default configuration (fallback values)
const DEFAULT_CONFIG: FullConfig = {
  apiUrl: 'https://pei-web-server.onrender.com/api/data',
  apiKey: '_6@L<Q*SC?mSdp$a1E4?L{"M+8QQ0|Cw',
  siteId: 1000,
  tenantId: 1001,
  ftpEnabled: false,
  ftpHost: '',
  ftpPath: '/',
  ftpPollInterval: 60
};

export class ConfigManager {
  private configPath: string;
  private config: FullConfig;

  constructor() {
    // Store config in ProgramData for service access
    const programData = process.env.PROGRAMDATA || 'C:\\ProgramData';
    const configDir = path.join(programData, 'PEI Site Service');

    // Ensure directory exists
    if (!fs.existsSync(configDir)) {
      fs.mkdirSync(configDir, { recursive: true });
    }

    this.configPath = path.join(configDir, 'config.json');
    this.config = this.loadConfig();
  }

  private loadConfig(): FullConfig {
    // First, try to load from config file
    if (fs.existsSync(this.configPath)) {
      try {
        const data = fs.readFileSync(this.configPath, 'utf8');
        const fileConfig = JSON.parse(data);
        console.log('[ConfigManager] Loaded config from file:', this.configPath);
        return { ...DEFAULT_CONFIG, ...fileConfig };
      } catch (err) {
        console.error('[ConfigManager] Error reading config file:', err);
      }
    }

    // Try to read from Windows registry (MSI installer values)
    const registryConfig = this.readFromRegistry();
    if (registryConfig) {
      // Save to file for future use
      this.saveConfig(registryConfig);
      return registryConfig;
    }

    // Fall back to defaults
    console.log('[ConfigManager] Using default configuration');
    return DEFAULT_CONFIG;
  }

  private readFromRegistry(): FullConfig | null {
    if (process.platform !== 'win32') {
      return null;
    }

    try {
      const registryPath64 = 'HKLM\\Software\\PEI Data Systems\\PEI Site App';
      const registryPath32 = 'HKLM\\Software\\WOW6432Node\\PEI Data Systems\\PEI Site App';

      let tenantId = this.readRegistryValue(registryPath32, 'TenantId') ||
                     this.readRegistryValue(registryPath64, 'TenantId');
      let siteId = this.readRegistryValue(registryPath32, 'SiteId') ||
                   this.readRegistryValue(registryPath64, 'SiteId');
      let ftpHost = this.readRegistryValue(registryPath32, 'FtpHost') ||
                    this.readRegistryValue(registryPath64, 'FtpHost');
      let ftpPath = this.readRegistryValue(registryPath32, 'FtpPath') ||
                    this.readRegistryValue(registryPath64, 'FtpPath');

      if (tenantId || siteId) {
        console.log('[ConfigManager] Found registry values:', { tenantId, siteId, ftpHost, ftpPath });
        return {
          ...DEFAULT_CONFIG,
          tenantId: tenantId ? parseInt(tenantId, 10) : DEFAULT_CONFIG.tenantId,
          siteId: siteId ? parseInt(siteId, 10) : DEFAULT_CONFIG.siteId,
          ftpHost: ftpHost || DEFAULT_CONFIG.ftpHost,
          ftpPath: ftpPath || DEFAULT_CONFIG.ftpPath,
          ftpEnabled: !!ftpHost
        };
      }
    } catch (err) {
      console.error('[ConfigManager] Error reading registry:', err);
    }

    return null;
  }

  private readRegistryValue(key: string, valueName: string): string | null {
    try {
      const result = execSync(`reg query "${key}" /v ${valueName}`, { encoding: 'utf8' });
      const match = result.match(/REG_SZ\s+(.+)/);
      return match ? match[1].trim() : null;
    } catch {
      return null;
    }
  }

  getConfig(): FullConfig {
    return { ...this.config };
  }

  getFtpConfig(): FtpConfig {
    return {
      ftpEnabled: this.config.ftpEnabled,
      ftpHost: this.config.ftpHost,
      ftpPath: this.config.ftpPath,
      ftpPollInterval: this.config.ftpPollInterval
    };
  }

  updateConfig(updates: Partial<FullConfig>): void {
    this.config = { ...this.config, ...updates };
    this.saveConfig(this.config);
  }

  private saveConfig(config: FullConfig): void {
    try {
      fs.writeFileSync(this.configPath, JSON.stringify(config, null, 2), 'utf8');
      console.log('[ConfigManager] Saved config to:', this.configPath);
    } catch (err) {
      console.error('[ConfigManager] Error saving config:', err);
    }
  }
}
