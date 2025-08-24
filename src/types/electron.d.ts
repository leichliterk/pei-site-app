export interface IElectronAPI {
  openFile: () => Promise<any>;
  saveFile: (data: any) => Promise<any>;
  minimize: () => Promise<void>;
  maximize: () => Promise<void>;
  close: () => Promise<void>;
  sendMessage: (message: string) => Promise<any>;
  onMessage: (callback: (message: string) => void) => void;
}

declare global {
  interface Window {
    electronAPI: IElectronAPI;
  }
}