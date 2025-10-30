import { Injectable } from '@angular/core';

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
}