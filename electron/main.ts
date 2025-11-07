import { app, BrowserWindow, Menu, shell, ipcMain, dialog } from 'electron';
import * as path from 'path';
import * as fs from 'fs';
import { isDev } from './utils';

// Windows registry module for startup management
const { execSync } = require('child_process');

let mainWindow: BrowserWindow | null;

function createWindow(): void {
  // Create the browser window
  mainWindow = new BrowserWindow({
    height: 800,
    width: 1200,
    minHeight: 600,
    minWidth: 800,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js')
    },
    icon: path.join(__dirname, '../src/assets/icon.png'), // Optional: app icon
    show: false, // Don't show until ready-to-show
    titleBarStyle: 'default'
  });

  // Load the app
  if (isDev()) {
    mainWindow.loadURL('http://localhost:4200');
    // mainWindow.webContents.openDevTools();
  } else {
    mainWindow.loadFile(path.join(__dirname, '../index.html'));
  }

  // Show window when ready
  mainWindow.once('ready-to-show', () => {
    if (mainWindow) {
      mainWindow.show();
    }
  });

  // Handle window closed
  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  // Handle external links
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  // Set application menu
  createMenu();
}

function createMenu(): void {
  const template = [
    {
      label: 'File',
      submenu: [
        {
          label: 'New',
          accelerator: 'CmdOrCtrl+N',
          click: () => {
            // Handle new file
          }
        },
        {
          label: 'Open',
          accelerator: 'CmdOrCtrl+O',
          click: () => {
            // Handle open file
          }
        },
        { type: 'separator' },
        {
          label: 'Exit',
          accelerator: process.platform === 'darwin' ? 'Cmd+Q' : 'Ctrl+Q',
          click: () => {
            app.quit();
          }
        }
      ]
    },
    {
      label: 'Edit',
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' }
      ]
    },
    {
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      label: 'Help',
      submenu: [
        {
          label: 'About',
          click: () => {
            // Show about dialog
          }
        }
      ]
    }
  ];

  const menu = Menu.buildFromTemplate(template as any);
  Menu.setApplicationMenu(menu);
}

// App event listeners
app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});


// IPC handlers
ipcMain.handle('dialog:openFile', async () => {
  if (!mainWindow) return { canceled: true };
  
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ['openFile'],
    filters: [
      { name: 'All Files', extensions: ['*'] },
      { name: 'Text Files', extensions: ['txt', 'md', 'json', 'js', 'ts', 'html', 'css'] },
      { name: 'Images', extensions: ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'svg', 'webp'] },
      { name: 'Documents', extensions: ['pdf', 'doc', 'docx', 'rtf'] },
      { name: 'Media', extensions: ['mp4', 'avi', 'mov', 'mp3', 'wav', 'flac'] },
      { name: 'Archives', extensions: ['zip', 'rar', '7z', 'tar', 'gz'] }
    ]
  });
  
  if (!result.canceled && result.filePaths.length > 0) {
    try {
      const filePath = result.filePaths[0];
      const stats = fs.statSync(filePath);
      return {
        ...result,
        fileSize: stats.size,
        lastModified: stats.mtime.toISOString()
      };
    } catch (error) {
      console.error('Error getting file stats:', error);
      return result;
    }
  }
  
  return result;
});

ipcMain.handle('window:minimize', () => {
  if (mainWindow) {
    mainWindow.minimize();
  }
});

ipcMain.handle('window:maximize', () => {
  if (mainWindow) {
    if (mainWindow.isMaximized()) {
      mainWindow.unmaximize();
    } else {
      mainWindow.maximize();
    }
  }
});

ipcMain.handle('window:close', () => {
  if (mainWindow) {
    mainWindow.close();
  }
});

// Settings persistence handlers
const userDataPath = app.getPath('userData');
const settingsFilePath = path.join(userDataPath, 'settings.json');

// Helper function to read Windows registry (Windows only)
function readWindowsRegistry(key: string, valueName: string): string | null {
  if (process.platform !== 'win32') return null;

  try {
    const result = execSync(`reg query "${key}" /v ${valueName}`, { encoding: 'utf8' });
    const match = result.match(/REG_SZ\s+(.+)/);
    return match ? match[1].trim() : null;
  } catch (error) {
    return null;
  }
}

// Helper function to check if app is set to start with Windows
function getStartupEnabled(): boolean {
  if (process.platform !== 'win32') return false;

  try {
    const appName = 'PDS Site App'; // Should match ProductName in installer
    const result = execSync(`reg query "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" /v "${appName}"`, { encoding: 'utf8' });
    return result.includes('pds-site-app.exe');
  } catch (error) {
    return false;
  }
}

// Helper function to enable/disable startup with Windows
function setStartupEnabled(enabled: boolean): boolean {
  if (process.platform !== 'win32') return false;

  try {
    const appName = 'PDS Site App'; // Should match ProductName in installer
    const exePath = process.execPath;

    if (enabled) {
      // Add to startup
      execSync(`reg add "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" /v "${appName}" /t REG_SZ /d "\\"${exePath}\\"" /f`, { encoding: 'utf8' });
    } else {
      // Remove from startup
      execSync(`reg delete "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" /v "${appName}" /f`, { encoding: 'utf8' });
    }
    return true;
  } catch (error) {
    console.error('Error setting startup preference:', error);
    return false;
  }
}

// Initialize settings from installer registry on first run
function initializeSettingsFromInstaller() {
  try {
    // Check if settings file already exists
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      const settings = JSON.parse(data);
      // If settings already have values, don't overwrite
      if (settings.tenantId || settings.siteNumber) {
        return;
      }
    }

    // Try to read from Windows registry (MSI installer values)
    const tenantId = readWindowsRegistry('HKLM\\Software\\PEI Data Systems\\PDS Site App', 'TenantId');
    const siteId = readWindowsRegistry('HKLM\\Software\\PEI Data Systems\\PDS Site App', 'SiteId');

    if (tenantId || siteId) {
      const settings: any = {};
      if (tenantId) settings.tenantId = tenantId;
      if (siteId) settings.siteNumber = parseInt(siteId, 10);

      fs.writeFileSync(settingsFilePath, JSON.stringify(settings, null, 2), 'utf8');
      console.log('Settings initialized from installer configuration');
    }
  } catch (error) {
    console.error('Error initializing settings from installer:', error);
  }
}

// Initialize settings on app startup
app.on('ready', () => {
  initializeSettingsFromInstaller();
});

ipcMain.handle('settings:getSiteNumber', async () => {
  try {
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      const settings = JSON.parse(data);
      return settings.siteNumber;
    }
  } catch (error) {
    console.error('Error reading site number:', error);
  }
  return null;
});

ipcMain.handle('settings:setSiteNumber', async (_event, siteNumber: number) => {
  try {
    let settings: any = {};
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      settings = JSON.parse(data);
    }
    settings.siteNumber = siteNumber;
    fs.writeFileSync(settingsFilePath, JSON.stringify(settings, null, 2), 'utf8');
    return true;
  } catch (error) {
    console.error('Error writing site number:', error);
    return false;
  }
});

ipcMain.handle('settings:getTenantId', async () => {
  try {
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      const settings = JSON.parse(data);
      return settings.tenantId;
    }
  } catch (error) {
    console.error('Error reading tenant ID:', error);
  }
  return null;
});

ipcMain.handle('settings:setTenantId', async (_event, tenantId: string) => {
  try {
    let settings: any = {};
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      settings = JSON.parse(data);
    }
    settings.tenantId = tenantId;
    fs.writeFileSync(settingsFilePath, JSON.stringify(settings, null, 2), 'utf8');
    return true;
  } catch (error) {
    console.error('Error writing tenant ID:', error);
    return false;
  }
});

// Startup preference handlers
ipcMain.handle('settings:getStartupEnabled', async () => {
  return getStartupEnabled();
});

ipcMain.handle('settings:setStartupEnabled', async (_event, enabled: boolean) => {
  return setStartupEnabled(enabled);
});