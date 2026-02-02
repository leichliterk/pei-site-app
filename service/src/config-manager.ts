import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';
import { ServiceConfig } from './websocket-client';

// Default configuration (fallback values)
const DEFAULT_CONFIG: ServiceConfig = {
  apiUrl: 'https://pei-web-server.onrender.com/api/data',
  apiKey: '_6@L<Q*SC?mSdp$a1E4?L{"M+8QQ0|Cw',
  siteId: 1000,
  tenantId: 1001
};

export class ConfigManager {
  private configPath: string;
  private config: ServiceConfig;

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

  private loadConfig(): ServiceConfig {
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

  private readFromRegistry(): ServiceConfig | null {
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

      if (tenantId || siteId) {
        console.log('[ConfigManager] Found registry values:', { tenantId, siteId });
        return {
          ...DEFAULT_CONFIG,
          tenantId: tenantId ? parseInt(tenantId, 10) : DEFAULT_CONFIG.tenantId,
          siteId: siteId ? parseInt(siteId, 10) : DEFAULT_CONFIG.siteId
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

  getConfig(): ServiceConfig {
    return { ...this.config };
  }

  updateConfig(updates: Partial<ServiceConfig>): void {
    this.config = { ...this.config, ...updates };
    this.saveConfig(this.config);
  }

  private saveConfig(config: ServiceConfig): void {
    try {
      fs.writeFileSync(this.configPath, JSON.stringify(config, null, 2), 'utf8');
      console.log('[ConfigManager] Saved config to:', this.configPath);
    } catch (err) {
      console.error('[ConfigManager] Error saving config:', err);
    }
  }
}
