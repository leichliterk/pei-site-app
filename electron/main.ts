import { app, BrowserWindow, Menu, shell, ipcMain, dialog } from 'electron';
import * as path from 'path';
import * as fs from 'fs';
import { isDev } from './utils';
import * as ftp from 'basic-ftp';

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
    mainWindow.loadURL('http://localhost:4300');
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

  // Remove the default application menu (File, Edit, etc.)
  Menu.setApplicationMenu(null);
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

// FTP Settings handlers
ipcMain.handle('settings:getFtpSettings', async () => {
  try {
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      const settings = JSON.parse(data);
      return settings.ftp || null;
    }
  } catch (error) {
    console.error('Error reading FTP settings:', error);
  }
  return null;
});

ipcMain.handle('settings:setFtpSettings', async (_event, ftpSettings: {
  host: string;
  path: string;
  scheduleMinutes: number;
  enabled: boolean;
}) => {
  try {
    let settings: any = {};
    if (fs.existsSync(settingsFilePath)) {
      const data = fs.readFileSync(settingsFilePath, 'utf8');
      settings = JSON.parse(data);
    }
    settings.ftp = ftpSettings;
    fs.writeFileSync(settingsFilePath, JSON.stringify(settings, null, 2), 'utf8');
    return true;
  } catch (error) {
    console.error('Error writing FTP settings:', error);
    return false;
  }
});

// Track downloaded files to avoid re-downloading
const downloadedFilesPath = path.join(userDataPath, 'downloaded-files.json');

function getDownloadedFiles(): string[] {
  try {
    if (fs.existsSync(downloadedFilesPath)) {
      const data = fs.readFileSync(downloadedFilesPath, 'utf8');
      return JSON.parse(data);
    }
  } catch (error) {
    console.error('Error reading downloaded files list:', error);
  }
  return [];
}

function saveDownloadedFiles(files: string[]): void {
  try {
    fs.writeFileSync(downloadedFilesPath, JSON.stringify(files, null, 2), 'utf8');
  } catch (error) {
    console.error('Error saving downloaded files list:', error);
  }
}

// Local download folder
const ftpDownloadPath = path.join(userDataPath, 'ftp-downloads');

// Ensure download directory exists
function ensureDownloadDir(): void {
  if (!fs.existsSync(ftpDownloadPath)) {
    fs.mkdirSync(ftpDownloadPath, { recursive: true });
  }
}

// FTP Operations
ipcMain.handle('ftp:testConnection', async (_event, host: string, remotePath: string) => {
  const client = new ftp.Client();
  client.ftp.verbose = false;

  try {
    await client.access({
      host: host,
      user: 'anonymous',
      password: 'anonymous@',
      secure: false
    });

    // Try to access the specified path
    await client.cd(remotePath);
    const list = await client.list();

    client.close();
    return {
      success: true,
      message: `Connected successfully. Found ${list.length} items in ${remotePath}`,
      fileCount: list.length
    };
  } catch (error: any) {
    client.close();
    return {
      success: false,
      message: error.message || 'Failed to connect to FTP server'
    };
  }
});

ipcMain.handle('ftp:syncFiles', async (_event, host: string, remotePath: string) => {
  const client = new ftp.Client();
  client.ftp.verbose = false;
  ensureDownloadDir();

  const downloadedFiles = getDownloadedFiles();
  const newlyDownloaded: string[] = [];
  const errors: string[] = [];

  try {
    await client.access({
      host: host,
      user: 'anonymous',
      password: 'anonymous@',
      secure: false
    });

    await client.cd(remotePath);
    const list = await client.list();

    // Filter for .txt files that haven't been downloaded yet
    const txtFiles = list.filter(item =>
      item.type === ftp.FileType.File &&
      item.name.toLowerCase().endsWith('.txt') &&
      !downloadedFiles.includes(item.name)
    );

    // Download each new file
    for (const file of txtFiles) {
      try {
        const localFilePath = path.join(ftpDownloadPath, file.name);
        await client.downloadTo(localFilePath, file.name);
        newlyDownloaded.push(file.name);
        downloadedFiles.push(file.name);
      } catch (fileError: any) {
        errors.push(`Failed to download ${file.name}: ${fileError.message}`);
      }
    }

    // Save updated downloaded files list
    saveDownloadedFiles(downloadedFiles);

    client.close();
    return {
      success: true,
      downloaded: newlyDownloaded,
      errors: errors,
      totalChecked: list.filter(i => i.name.toLowerCase().endsWith('.txt')).length,
      message: newlyDownloaded.length > 0
        ? `Downloaded ${newlyDownloaded.length} new file(s)`
        : 'No new files to download'
    };
  } catch (error: any) {
    client.close();
    return {
      success: false,
      downloaded: newlyDownloaded,
      errors: [...errors, error.message],
      message: error.message || 'Failed to sync files from FTP server'
    };
  }
});

ipcMain.handle('ftp:getDownloadedFiles', async () => {
  return getDownloadedFiles();
});

ipcMain.handle('ftp:getLocalFiles', async () => {
  ensureDownloadDir();
  try {
    const files = fs.readdirSync(ftpDownloadPath);
    return files.map(name => {
      const filePath = path.join(ftpDownloadPath, name);
      const stats = fs.statSync(filePath);
      return {
        name,
        size: stats.size,
        modified: stats.mtime.toISOString()
      };
    });
  } catch (error) {
    console.error('Error reading local files:', error);
    return [];
  }
});

ipcMain.handle('ftp:clearDownloadHistory', async () => {
  try {
    saveDownloadedFiles([]);
    return true;
  } catch (error) {
    console.error('Error clearing download history:', error);
    return false;
  }
});