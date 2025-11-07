# MSI Installer Setup Guide

This guide explains how to build and customize the MSI installer for the PEI Site App.

## Prerequisites

1. **Windows Operating System** - MSI installers can only be built on Windows
2. **WiX Toolset** - Download and install from [https://wixtoolset.org/](https://wixtoolset.org/)
   - Install WiX Toolset 3.11 or later
   - Make sure the WiX binaries are in your PATH

## Configuration Steps

### 1. Generate Unique GUIDs

Before building the MSI, you need to generate unique GUIDs for your application:

```powershell
# In PowerShell, run:
[guid]::NewGuid()
```

Run this command twice to generate two different GUIDs.

### 2. Update Configuration Files

#### Update `build-msi.js`:
- Replace `YOUR-UPGRADE-CODE-GUID-HERE` with the first GUID you generated
- Update `manufacturer` field with your company name
- Update `name` and `description` fields as needed
- Update `exe` field to match your executable name (currently 'pei-site-app')

#### Update `installer/msi-template.wxs`:
- Replace `YOUR-UPGRADE-CODE-GUID-HERE` with the same GUID used in build-msi.js
- Update the manufacturer name in the registry paths to match your company name

#### Update `electron/main.ts`:
- Update the registry path in the `readWindowsRegistry` calls (around line 230-231)
- Replace 'Your Company Name' with your actual company name
- Update the `appName` variable in `getStartupEnabled()` and `setStartupEnabled()` functions (lines 223, 236) to match your ProductName

### 3. Update package.json

Update the product metadata in `package.json`:

```json
"build": {
  "appId": "com.yourcompany.pei-site-app",
  "productName": "PEI Site App",
  ...
}
```

## Building the MSI

### Step 1: Build the Windows Electron Package

```bash
npm run electron:build:win
```

This command will:
1. Compile the Electron TypeScript files
2. Build the Angular app in production mode
3. Create an unpacked Windows executable in `release/win-unpacked`

### Step 2: Create the MSI Installer

```bash
npm run electron:build:msi
```

Or run both steps together:
```bash
npm run electron:build:win && npm run electron:build:msi
```

The MSI file will be created in the `release/msi` directory.

## How the Installer Works

### Installation Flow

1. **User runs the MSI installer**
2. **Installation directory selection** - User chooses where to install
3. **Configuration dialog appears** - User enters:
   - Tenant ID
   - Site ID
   - Start with Windows checkbox (checked by default)
4. **Files are installed**
5. **Registry values are written**:
   - Configuration stored at `HKLM\Software\[Manufacturer]\[ProductName]`
   - Startup entry (if enabled) at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
6. **Desktop shortcuts are created**

### First Run Behavior

When the application runs for the first time:
1. It checks if `settings.json` exists in the user data directory
2. If not (or if values are empty), it reads the Tenant ID and Site ID from the Windows Registry
3. These values are copied to `settings.json` for persistent storage
4. The app can then access these values through the Electron service

### Settings Storage Locations

1. **During Installation**: Registry at `HKLM\Software\[Manufacturer]\[ProductName]`
   - `TenantId` (REG_SZ)
   - `SiteId` (REG_SZ)

2. **After First Run**: JSON file at `%APPDATA%\pei-site-app\settings.json`
   ```json
   {
     "tenantId": "your-tenant-id",
     "siteNumber": 123
   }
   ```

## Accessing Configuration in Your App

The Electron service provides methods to access these values:

```typescript
// In any Angular component
constructor(private electronService: ElectronService) {}

async ngOnInit() {
  const tenantId = await this.electronService.getTenantId();
  const siteNumber = await this.electronService.getSiteNumber();
  const startupEnabled = await this.electronService.getStartupEnabled();

  console.log('Tenant ID:', tenantId);
  console.log('Site Number:', siteNumber);
  console.log('Start with Windows:', startupEnabled);
}

// To enable/disable startup with Windows programmatically
async toggleStartup(enabled: boolean) {
  const success = await this.electronService.setStartupEnabled(enabled);
  if (success) {
    console.log(`Startup ${enabled ? 'enabled' : 'disabled'} successfully`);
  }
}
```

### Managing Startup with Windows

Users can control whether the app starts with Windows in two ways:

1. **During Installation**: Check/uncheck the "Start with Windows" option
2. **After Installation**: Use the app's settings UI to call `setStartupEnabled()`

The startup setting is stored in the Windows registry at:
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Customizing the Installer

### Add Custom Dialogs

Edit `installer/msi-template.wxs` to add more dialogs or input fields.

### Change Installer Branding

Add custom images to the `installer` directory:
- `background.png` (493x312 pixels) - Background image for installer
- `banner.png` (493x58 pixels) - Banner image at top of installer

Update `build-msi.js` to reference these images.

### Add Installation Conditions

You can add prerequisites or conditions in the WiX template:

```xml
<!-- Example: Require .NET Framework -->
<PropertyRef Id="NETFRAMEWORK45"/>
<Condition Message="This application requires .NET Framework 4.5 or higher.">
  <![CDATA[Installed OR NETFRAMEWORK45]]>
</Condition>
```

## Troubleshooting

### WiX Toolset Not Found
- Make sure WiX is installed and its bin directory is in your PATH
- Restart your terminal/IDE after installing WiX

### MSI Build Fails
- Ensure you ran `npm run electron:build:win` first
- Check that the `release/win-unpacked` directory exists and contains your app
- Verify all GUIDs are valid and properly formatted

### App Doesn't Read Configuration
- Check that the registry path in `main.ts` matches the path in the WiX template
- Verify the Manufacturer and ProductName match exactly (case-sensitive)
- Run the installer as Administrator if registry writes fail

### Registry Values Not Persisting
- The installer must be run with administrator privileges to write to HKLM
- Consider using HKCU (current user) if admin access is a problem

### Startup with Windows Not Working
- Check that the app name in `main.ts` matches the ProductName exactly
- Verify the registry entry exists at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Run `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"` in CMD to verify
- The executable path must be quoted properly if it contains spaces

## Testing

1. **Build the MSI**: `npm run electron:build:msi`
2. **Uninstall any previous version** from Windows Settings
3. **Run the MSI installer** as Administrator
4. **Enter test values** for Tenant ID and Site ID
5. **Verify "Start with Windows" is checked** (or uncheck to test disabled state)
6. **Complete installation**
7. **Launch the app** and verify the settings are loaded
8. **Check settings file** at `%APPDATA%\pei-site-app\settings.json`
9. **Restart Windows** to verify the app starts automatically (if enabled)
10. **Check startup registry** with: `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"`

## Distribution

Once built, the MSI file can be:
- Distributed directly to users
- Hosted on a download server
- Deployed via enterprise software distribution tools (SCCM, Intune, etc.)
- Signed with a code signing certificate for better trust

## Code Signing (Optional but Recommended)

To sign your MSI:

1. Obtain a code signing certificate
2. Update `build-msi.js`:
   ```javascript
   signWithParams: '/a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256'
   ```

This will make Windows trust your installer and reduce security warnings.
