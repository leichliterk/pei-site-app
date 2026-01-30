import { contextBridge, ipcRenderer } from 'electron';

// FTP Settings interface
interface FtpSettings {
  host: string;
  path: string;
  scheduleMinutes: number;
  enabled: boolean;
}

interface FtpSyncResult {
  success: boolean;
  downloaded: string[];
  errors: string[];
  totalChecked?: number;
  message: string;
}

interface FtpTestResult {
  success: boolean;
  message: string;
  fileCount?: number;
}

interface LocalFile {
  name: string;
  size: number;
  modified: string;
}

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  openFile: () => ipcRenderer.invoke('dialog:openFile'),
  saveFile: (data: any) => ipcRenderer.invoke('dialog:saveFile', data),
  minimize: () => ipcRenderer.invoke('window:minimize'),
  maximize: () => ipcRenderer.invoke('window:maximize'),
  close: () => ipcRenderer.invoke('window:close'),

  // Settings persistence
  getSiteNumber: () => ipcRenderer.invoke('settings:getSiteNumber'),
  setSiteNumber: (siteNumber: number) => ipcRenderer.invoke('settings:setSiteNumber', siteNumber),
  getTenantId: () => ipcRenderer.invoke('settings:getTenantId'),
  setTenantId: (tenantId: string) => ipcRenderer.invoke('settings:setTenantId', tenantId),
  getSiteName: () => ipcRenderer.invoke('settings:getSiteName'),
  setSiteName: (siteName: string) => ipcRenderer.invoke('settings:setSiteName', siteName),
  getStartupEnabled: () => ipcRenderer.invoke('settings:getStartupEnabled'),
  setStartupEnabled: (enabled: boolean) => ipcRenderer.invoke('settings:setStartupEnabled', enabled),

  // FTP Settings
  getFtpSettings: () => ipcRenderer.invoke('settings:getFtpSettings'),
  setFtpSettings: (settings: FtpSettings) => ipcRenderer.invoke('settings:setFtpSettings', settings),

  // FTP Operations
  ftpTestConnection: (host: string, path: string) => ipcRenderer.invoke('ftp:testConnection', host, path),
  ftpSyncFiles: (host: string, path: string) => ipcRenderer.invoke('ftp:syncFiles', host, path),
  ftpGetDownloadedFiles: () => ipcRenderer.invoke('ftp:getDownloadedFiles'),
  ftpGetLocalFiles: () => ipcRenderer.invoke('ftp:getLocalFiles'),
  ftpClearDownloadHistory: () => ipcRenderer.invoke('ftp:clearDownloadHistory'),

  // Example of exposing a method to send messages to main process
  sendMessage: (message: string) => ipcRenderer.invoke('app:message', message),

  // Example of listening to messages from main process
  onMessage: (callback: (message: string) => void) => {
    ipcRenderer.on('app:message', (event, message) => callback(message));
  },

  // Navigation from tray menu
  onNavigate: (callback: (route: string) => void) => {
    ipcRenderer.on('app:navigate', (event, route) => callback(route));
  }
});

// Type definitions for TypeScript
declare global {
  interface Window {
    electronAPI: {
      openFile: () => Promise<any>;
      saveFile: (data: any) => Promise<any>;
      minimize: () => Promise<void>;
      maximize: () => Promise<void>;
      close: () => Promise<void>;
      getSiteNumber: () => Promise<number | null>;
      setSiteNumber: (siteNumber: number) => Promise<boolean>;
      getTenantId: () => Promise<string | null>;
      setTenantId: (tenantId: string) => Promise<boolean>;
      getSiteName: () => Promise<string | null>;
      setSiteName: (siteName: string) => Promise<boolean>;
      getStartupEnabled: () => Promise<boolean>;
      setStartupEnabled: (enabled: boolean) => Promise<boolean>;
      // FTP Settings
      getFtpSettings: () => Promise<FtpSettings | null>;
      setFtpSettings: (settings: FtpSettings) => Promise<boolean>;
      // FTP Operations
      ftpTestConnection: (host: string, path: string) => Promise<FtpTestResult>;
      ftpSyncFiles: (host: string, path: string) => Promise<FtpSyncResult>;
      ftpGetDownloadedFiles: () => Promise<string[]>;
      ftpGetLocalFiles: () => Promise<LocalFile[]>;
      ftpClearDownloadHistory: () => Promise<boolean>;
      sendMessage: (message: string) => Promise<any>;
      onMessage: (callback: (message: string) => void) => void;
      onNavigate: (callback: (route: string) => void) => void;
    };
  }
}