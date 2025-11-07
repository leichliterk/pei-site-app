import { contextBridge, ipcRenderer } from 'electron';

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
  getStartupEnabled: () => ipcRenderer.invoke('settings:getStartupEnabled'),
  setStartupEnabled: (enabled: boolean) => ipcRenderer.invoke('settings:setStartupEnabled', enabled),

  // Example of exposing a method to send messages to main process
  sendMessage: (message: string) => ipcRenderer.invoke('app:message', message),

  // Example of listening to messages from main process
  onMessage: (callback: (message: string) => void) => {
    ipcRenderer.on('app:message', (event, message) => callback(message));
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
      getStartupEnabled: () => Promise<boolean>;
      setStartupEnabled: (enabled: boolean) => Promise<boolean>;
      sendMessage: (message: string) => Promise<any>;
      onMessage: (callback: (message: string) => void) => void;
    };
  }
}