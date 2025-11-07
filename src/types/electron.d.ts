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
  sendMessage: (message: string) => Promise<any>;
  onMessage: (callback: (message: string) => void) => void;
}

declare global {
  interface Window {
    electronAPI: IElectronAPI;
  }
}