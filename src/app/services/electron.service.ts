import { Injectable } from '@angular/core';

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

@Injectable({
  providedIn: 'root'
})
export class ElectronService {
  private isElectron = false;

  constructor() {
    this.isElectron = !!(window && window.electronAPI);
  }

  get isElectronApp(): boolean {
    return this.isElectron;
  }

  async openFile(): Promise<any> {
    if (this.isElectron) {
      return window.electronAPI.openFile();
    }
    return null;
  }

  async saveFile(data: any): Promise<any> {
    if (this.isElectron) {
      return window.electronAPI.saveFile(data);
    }
    return null;
  }

  minimizeWindow(): void {
    if (this.isElectron) {
      window.electronAPI.minimize();
    }
  }

  maximizeWindow(): void {
    if (this.isElectron) {
      window.electronAPI.maximize();
    }
  }

  closeWindow(): void {
    if (this.isElectron) {
      window.electronAPI.close();
    }
  }

  async getSiteNumber(): Promise<number | null> {
    if (this.isElectron) {
      return window.electronAPI.getSiteNumber();
    }
    return null;
  }

  async setSiteNumber(siteNumber: number): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.setSiteNumber(siteNumber);
    }
    return false;
  }

  async getTenantId(): Promise<string | null> {
    if (this.isElectron) {
      return window.electronAPI.getTenantId();
    }
    return null;
  }

  async setTenantId(tenantId: string): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.setTenantId(tenantId);
    }
    return false;
  }

  async getStartupEnabled(): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.getStartupEnabled();
    }
    return false;
  }

  async setStartupEnabled(enabled: boolean): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.setStartupEnabled(enabled);
    }
    return false;
  }

  // FTP Settings
  async getFtpSettings(): Promise<FtpSettings | null> {
    if (this.isElectron) {
      return window.electronAPI.getFtpSettings();
    }
    return null;
  }

  async setFtpSettings(settings: FtpSettings): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.setFtpSettings(settings);
    }
    return false;
  }

  // FTP Operations
  async ftpTestConnection(host: string, path: string): Promise<FtpTestResult> {
    if (this.isElectron) {
      return window.electronAPI.ftpTestConnection(host, path);
    }
    return { success: false, message: 'Not running in Electron' };
  }

  async ftpSyncFiles(host: string, path: string): Promise<FtpSyncResult> {
    if (this.isElectron) {
      return window.electronAPI.ftpSyncFiles(host, path);
    }
    return { success: false, downloaded: [], errors: ['Not running in Electron'], message: 'Not running in Electron' };
  }

  async ftpGetDownloadedFiles(): Promise<string[]> {
    if (this.isElectron) {
      return window.electronAPI.ftpGetDownloadedFiles();
    }
    return [];
  }

  async ftpGetLocalFiles(): Promise<LocalFile[]> {
    if (this.isElectron) {
      return window.electronAPI.ftpGetLocalFiles();
    }
    return [];
  }

  async ftpClearDownloadHistory(): Promise<boolean> {
    if (this.isElectron) {
      return window.electronAPI.ftpClearDownloadHistory();
    }
    return false;
  }
}