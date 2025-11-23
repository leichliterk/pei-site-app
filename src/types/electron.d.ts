export interface FtpSettings {
  host: string;
  path: string;
  scheduleMinutes: number;
  enabled: boolean;
}

export interface FtpSyncResult {
  success: boolean;
  downloaded: string[];
  errors: string[];
  totalChecked?: number;
  message: string;
}

export interface FtpTestResult {
  success: boolean;
  message: string;
  fileCount?: number;
}

export interface LocalFile {
  name: string;
  size: number;
  modified: string;
}

export interface IElectronAPI {
  openFile: () => Promise<any>;
  saveFile: (data: any) => Promise<any>;
  minimize: () => Promise<void>;
  maximize: () => Promise<void>;
  close: () => Promise<void>;
  getSiteNumber: () => Promise<number | null>;
  setSiteNumber: (siteNumber: number) => Promise<boolean>;
  getTenantId: () => Promise<string | null>;
  setTenantId: (tenantId: string) => Promise<boolean>;
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
}

declare global {
  interface Window {
    electronAPI: IElectronAPI;
  }
}