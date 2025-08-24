import { contextBridge, ipcRenderer } from 'electron';

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  openFile: () => ipcRenderer.invoke('dialog:openFile'),
  saveFile: (data: any) => ipcRenderer.invoke('dialog:saveFile', data),
  minimize: () => ipcRenderer.invoke('window:minimize'),
  maximize: () => ipcRenderer.invoke('window:maximize'),
  close: () => ipcRenderer.invoke('window:close'),
  
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
      sendMessage: (message: string) => Promise<any>;
      onMessage: (callback: (message: string) => void) => void;
    };
  }
}