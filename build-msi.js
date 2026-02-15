const path = require('path');
const { execSync } = require('child_process');
const fs = require('fs');
function buildMSI() {
  const isStaging = process.argv.includes('staging');
  const envSuffix = isStaging ? ' - Staging' : '';
  const outDirSuffix = isStaging ? '-staging' : '';

  // Add WiX to PATH if not already present
  const wixPath = 'C:\\Program Files (x86)\\WiX Toolset v3.14\\bin';
  if (!process.env.PATH.includes(wixPath)) {
    process.env.PATH = `${process.env.PATH};${wixPath}`;
    console.log(`Added WiX to PATH: ${wixPath}`);
  }

  // Read version from AppSettings.cs
  const appSettingsPath = path.resolve(__dirname, 'service-dotnet', 'PeiSiteApp', 'Models', 'AppSettings.cs');
  const appSettingsContent = fs.readFileSync(appSettingsPath, 'utf8');
  const versionMatch = appSettingsContent.match(/Version\s*\{[^}]*\}\s*=\s*"([^"]+)"/);
  const appVersion = versionMatch ? versionMatch[1] : '1.0.0';
  const appName = `PEI Site App${envSuffix}`;

  console.log(`Building MSI for ${isStaging ? 'STAGING' : 'PRODUCTION'} environment (v${appVersion})...`);

  // Build the C# service executable
  console.log('\n--- Building background service (dotnet publish) ---');
  execSync('dotnet publish -c Release', {
    cwd: path.resolve(__dirname, 'service-dotnet', 'PeiSiteService'),
    stdio: 'inherit'
  });
  console.log('Service executable built successfully!');

  // Build the WPF app executable
  console.log('\n--- Building WPF app (dotnet publish) ---');
  execSync('dotnet publish -c Release', {
    cwd: path.resolve(__dirname, 'service-dotnet', 'PeiSiteApp'),
    stdio: 'inherit'
  });
  console.log('WPF app built successfully!');

  // Paths
  const servicePublishDir = path.resolve(__dirname, 'service-dotnet', 'PeiSiteService', 'bin', 'Release', 'net8.0-windows', 'win-x64', 'publish');
  const appPublishDir = path.resolve(__dirname, 'service-dotnet', 'PeiSiteApp', 'bin', 'Release', 'net8.0-windows', 'win-x64', 'publish');
  const outDir = path.resolve(__dirname, 'release', `msi${outDirSuffix}`);
  const iconPath = path.resolve(__dirname, 'service-dotnet', 'PeiSiteApp', 'Resources', 'icon.ico');

  // Ensure output directory exists
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  console.log('\n--- Collecting files ---');
  console.log(`Service publish dir: ${servicePublishDir}`);
  console.log(`App publish dir: ${appPublishDir}`);

  // Collect all files from publish directories
  function getFiles(dir) {
    const files = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isFile()) {
        files.push(entry.name);
      }
    }
    return files;
  }

  const serviceFiles = getFiles(servicePublishDir);
  const appFiles = getFiles(appPublishDir);
  console.log(`Service files: ${serviceFiles.join(', ')}`);
  console.log(`App files: ${appFiles.join(', ')}`);

  // Generate WiX component entries for files
  // The service exe gets special handling (ServiceInstall/ServiceControl)
  // The app exe gets shortcut handling
  function fileId(name) {
    // WiX IDs must be alphanumeric + underscore, max 72 chars
    return name.replace(/[^a-zA-Z0-9_]/g, '_').substring(0, 70);
  }

  // Build component XML for app files (excluding service exe if present)
  let appComponentsXml = '';
  let appComponentRefs = '';
  for (const file of appFiles) {
    const id = `App_${fileId(file)}`;
    const source = path.join(appPublishDir, file);
    if (file === 'pei-site-app.exe') {
      // Main app exe — includes shortcuts
      appComponentsXml += `
      <Component Id="${id}" Guid="*">
        <File Id="${id}_file" Name="${file}" Source="${source}" KeyPath="yes" />
      </Component>
`;
    } else {
      appComponentsXml += `
      <Component Id="${id}" Guid="*">
        <File Id="${id}_file" Name="${file}" Source="${source}" KeyPath="yes" />
      </Component>
`;
    }
    appComponentRefs += `        <ComponentRef Id="${id}" />\n`;
  }

  // Build component XML for service files
  let serviceComponentsXml = '';
  let serviceComponentRefs = '';
  for (const file of serviceFiles) {
    const id = `Svc_${fileId(file)}`;
    const source = path.join(servicePublishDir, file);
    if (file === 'pei-site-service.exe') {
      // Service exe — includes ServiceInstall/ServiceControl
      serviceComponentsXml += `
      <Component Id="${id}" Guid="*">
        <File Id="${id}_file" Name="${file}" Source="${source}" KeyPath="yes" />
        <ServiceInstall Id="PeiSiteServiceInstall"
                        Name="PeiSiteService"
                        DisplayName="PEI Site Service"
                        Description="Background service for PEI Site App monitoring"
                        Start="auto"
                        Type="ownProcess"
                        Account="LocalSystem"
                        ErrorControl="normal" />
        <ServiceControl Id="PeiSiteServiceControl"
                        Name="PeiSiteService"
                        Start="install"
                        Stop="both"
                        Remove="uninstall"
                        Wait="yes" />
      </Component>
`;
    } else {
      serviceComponentsXml += `
      <Component Id="${id}" Guid="*">
        <File Id="${id}_file" Name="${file}" Source="${source}" KeyPath="yes" />
      </Component>
`;
    }
    serviceComponentRefs += `        <ComponentRef Id="${id}" />\n`;
  }

  // Generate the complete WiX source
  const wxsContent = `<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*"
           Name="${appName}"
           Language="1033"
           Version="${appVersion}"
           Manufacturer="PEI Data Systems"
           UpgradeCode="57f48daa-3001-40a9-9dab-5a20450fd982">

    <Package InstallerVersion="500" Compressed="yes" InstallScope="perMachine"
             Description="${appName} Installer" Comments="Installs ${appName} v${appVersion}" />

    <MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />
    <MediaTemplate EmbedCab="yes" CompressionLevel="high" />

    <Property Id="ARPPRODUCTICON" Value="AppIcon.exe" />
    <Property Id="VisibleProductName" Value="${appName}" />

    <!-- Custom properties for site configuration -->
    <Property Id="TENANT_ID" Secure="yes">
      <RegistrySearch Id="TenantIdSearch" Root="HKLM"
                     Key="Software\\PEI Data Systems\\PEI Site App"
                     Name="TenantId" Type="raw" />
    </Property>
    <Property Id="SITE_ID" Secure="yes">
      <RegistrySearch Id="SiteIdSearch" Root="HKLM"
                     Key="Software\\PEI Data Systems\\PEI Site App"
                     Name="SiteId" Type="raw" />
    </Property>
    <Property Id="SITE_NAME" Secure="yes">
      <RegistrySearch Id="SiteNameSearch" Root="HKLM"
                     Key="Software\\PEI Data Systems\\PEI Site App"
                     Name="SiteName" Type="raw" />
    </Property>
    <Property Id="START_WITH_WINDOWS" Value="1" />

    <Icon Id="AppIcon.exe" SourceFile="${iconPath}" />

    <!-- Directory structure -->
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="ProgramFilesFolder">
        <Directory Id="ManufacturerFolder" Name="PEI Data Systems">
          <Directory Id="APPLICATIONROOTDIRECTORY" Name="${appName}" />
        </Directory>
      </Directory>
      <Directory Id="ProgramMenuFolder">
        <Directory Id="ApplicationProgramsFolder" Name="${appName}" />
      </Directory>
      <Directory Id="DesktopFolder" />
    </Directory>

    <!-- App files -->
    <DirectoryRef Id="APPLICATIONROOTDIRECTORY">
${appComponentsXml}
${serviceComponentsXml}
      <!-- Registry entries for configuration -->
      <Component Id="ConfigRegistryEntries" Guid="*">
        <RegistryKey Root="HKLM" Key="Software\\PEI Data Systems\\PEI Site App" ForceCreateOnInstall="yes">
          <RegistryValue Type="string" Name="TenantId" Value="[TENANT_ID]" KeyPath="yes" />
          <RegistryValue Type="string" Name="SiteId" Value="[SITE_ID]" />
          <RegistryValue Type="string" Name="SiteName" Value="[SITE_NAME]" />
          <RegistryValue Type="string" Name="InstallPath" Value="[APPLICATIONROOTDIRECTORY]" />
        </RegistryKey>
      </Component>

      <!-- Windows startup entry -->
      <Component Id="StartupRegistryEntry" Guid="*">
        <Condition>START_WITH_WINDOWS = "1"</Condition>
        <RegistryKey Root="HKCU" Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run">
          <RegistryValue Type="string" Name="${appName}" Value="&quot;[APPLICATIONROOTDIRECTORY]pei-site-app.exe&quot;" KeyPath="yes" />
        </RegistryKey>
      </Component>

      <!-- App icon for Add/Remove Programs -->
      <Component Id="AppIconFile" Guid="*">
        <File Id="AppIconIco" Name="app-icon.ico" Source="${iconPath}" KeyPath="yes" />
      </Component>
    </DirectoryRef>

    <!-- Start Menu shortcuts -->
    <DirectoryRef Id="ApplicationProgramsFolder">
      <Component Id="ApplicationShortcut" Guid="*">
        <Shortcut Id="ApplicationStartMenuShortcut"
                  Name="${appName}"
                  Description="${appName}"
                  Target="[APPLICATIONROOTDIRECTORY]pei-site-app.exe"
                  WorkingDirectory="APPLICATIONROOTDIRECTORY"
                  Icon="AppIcon.exe"
                  IconIndex="0" />
        <RemoveFolder Id="CleanUpStartMenu" Directory="ApplicationProgramsFolder" On="uninstall" />
        <RegistryValue Root="HKCU" Key="Software\\PEI Data Systems\\${appName}" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </DirectoryRef>

    <!-- Desktop shortcut -->
    <DirectoryRef Id="DesktopFolder">
      <Component Id="DesktopShortcut" Guid="*">
        <Shortcut Id="ApplicationDesktopShortcut"
                  Name="${appName}"
                  Description="${appName}"
                  Target="[APPLICATIONROOTDIRECTORY]pei-site-app.exe"
                  WorkingDirectory="APPLICATIONROOTDIRECTORY"
                  Icon="AppIcon.exe"
                  IconIndex="0" />
        <RegistryValue Root="HKCU" Key="Software\\PEI Data Systems\\${appName}" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </DirectoryRef>

    <!-- Feature definition -->
    <Feature Id="MainApplication" Title="${appName}" Level="1">
${appComponentRefs}${serviceComponentRefs}        <ComponentRef Id="ConfigRegistryEntries" />
        <ComponentRef Id="StartupRegistryEntry" />
        <ComponentRef Id="AppIconFile" />
        <ComponentRef Id="ApplicationShortcut" />
        <ComponentRef Id="DesktopShortcut" />
    </Feature>

    <!-- UI Configuration -->
    <UI>
      <UIRef Id="WixUI_InstallDir" />
      <Property Id="WIXUI_INSTALLDIR" Value="APPLICATIONROOTDIRECTORY" />

      <Property Id="WIXUI_EXITDIALOGOPTIONALTEXT" Value="The PEI Site Service has been installed and started. The background service will maintain the connection to your monitoring server automatically." />

      <!-- Custom Configuration Dialog -->
      <Dialog Id="ConfigurationDlg" Width="370" Height="270" Title="[ProductName] Setup">
        <Control Id="BottomLine" Type="Line" X="0" Y="234" Width="370" Height="0" />

        <Control Id="Title" Type="Text" X="15" Y="6" Width="340" Height="20" NoPrefix="yes" Text="{\\WixUI_Font_Title}Site Configuration" />
        <Control Id="Description" Type="Text" X="15" Y="26" Width="340" Height="20" NoPrefix="yes" Text="Please enter your site configuration details." />

        <Control Id="TenantIdLabel" Type="Text" X="20" Y="55" Width="100" Height="15" NoPrefix="yes" Text="Tenant ID:" />
        <Control Id="TenantIdEdit" Type="Edit" X="20" Y="70" Width="330" Height="18" Property="TENANT_ID" />

        <Control Id="SiteIdLabel" Type="Text" X="20" Y="95" Width="100" Height="15" NoPrefix="yes" Text="Site ID:" />
        <Control Id="SiteIdEdit" Type="Edit" X="20" Y="110" Width="330" Height="18" Property="SITE_ID" />

        <Control Id="SiteNameLabel" Type="Text" X="20" Y="135" Width="100" Height="15" NoPrefix="yes" Text="Site Name:" />
        <Control Id="SiteNameEdit" Type="Edit" X="20" Y="150" Width="330" Height="18" Property="SITE_NAME" />

        <Control Id="StartWithWindowsCheckbox" Type="CheckBox" X="20" Y="180" Width="330" Height="17" Property="START_WITH_WINDOWS" CheckBoxValue="1" Text="Start application automatically when Windows starts" />

        <Control Id="Back" Type="PushButton" X="180" Y="243" Width="56" Height="17" Text="Back" />
        <Control Id="Next" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="Next" />
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="Cancel">
          <Publish Event="SpawnDialog" Value="CancelDlg">1</Publish>
        </Control>
      </Dialog>

      <!-- Dialog flow: Welcome -> Configuration -> InstallDir -> ... -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ConfigurationDlg" Order="99">NOT Installed</Publish>
      <Publish Dialog="ConfigurationDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg">1</Publish>
      <Publish Dialog="ConfigurationDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg">1</Publish>
      <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="ConfigurationDlg" Order="99">1</Publish>
    </UI>
    <UIRef Id="WixUI_ErrorProgressText" />

  </Product>
</Wix>
`;

  // Write WiX source file
  const wxsPath = path.join(outDir, 'PEI Site App.wxs');
  fs.writeFileSync(wxsPath, wxsContent, 'utf8');
  console.log(`\nWiX source written to: ${wxsPath}`);

  // Compile with candle.exe
  console.log('\n--- Compiling WiX source (candle.exe) ---');
  const wixobjPath = path.join(outDir, 'PEI Site App.wixobj');
  execSync(`candle.exe -nologo -ext WixUIExtension -ext WixUtilExtension -out "${wixobjPath}" "${wxsPath}"`, {
    stdio: 'inherit'
  });

  // Link with light.exe
  console.log('\n--- Linking MSI (light.exe) ---');
  const msiPath = path.join(outDir, `PEI Site App${envSuffix}.msi`);
  execSync(`light.exe -nologo -ext WixUIExtension -ext WixUtilExtension -sice:ICE61 -out "${msiPath}" "${wixobjPath}"`, {
    stdio: 'inherit'
  });

  console.log(`\nMSI installer created successfully!`);
  console.log(`Output: ${msiPath}`);
}

buildMSI();
