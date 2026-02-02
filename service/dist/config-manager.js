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
exports.ConfigManager = void 0;
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const child_process_1 = require("child_process");
// Default configuration (fallback values)
const DEFAULT_CONFIG = {
    apiUrl: 'https://pei-web-server.onrender.com/api/data',
    apiKey: '_6@L<Q*SC?mSdp$a1E4?L{"M+8QQ0|Cw',
    siteId: 1000,
    tenantId: 1001
};
class ConfigManager {
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
    loadConfig() {
        // First, try to load from config file
        if (fs.existsSync(this.configPath)) {
            try {
                const data = fs.readFileSync(this.configPath, 'utf8');
                const fileConfig = JSON.parse(data);
                console.log('[ConfigManager] Loaded config from file:', this.configPath);
                return { ...DEFAULT_CONFIG, ...fileConfig };
            }
            catch (err) {
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
    readFromRegistry() {
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
        }
        catch (err) {
            console.error('[ConfigManager] Error reading registry:', err);
        }
        return null;
    }
    readRegistryValue(key, valueName) {
        try {
            const result = (0, child_process_1.execSync)(`reg query "${key}" /v ${valueName}`, { encoding: 'utf8' });
            const match = result.match(/REG_SZ\s+(.+)/);
            return match ? match[1].trim() : null;
        }
        catch {
            return null;
        }
    }
    getConfig() {
        return { ...this.config };
    }
    updateConfig(updates) {
        this.config = { ...this.config, ...updates };
        this.saveConfig(this.config);
    }
    saveConfig(config) {
        try {
            fs.writeFileSync(this.configPath, JSON.stringify(config, null, 2), 'utf8');
            console.log('[ConfigManager] Saved config to:', this.configPath);
        }
        catch (err) {
            console.error('[ConfigManager] Error saving config:', err);
        }
    }
}
exports.ConfigManager = ConfigManager;
//# sourceMappingURL=config-manager.js.map