const { MSICreator } = require('electron-wix-msi');
const path = require('path');
const { execSync } = require('child_process');
const fs = require('fs');

async function buildMSI() {
  // Check if staging environment is requested
  const isStaging = process.argv.includes('staging');
  const envSuffix = isStaging ? ' (Staging)' : '';
  const outDirSuffix = isStaging ? '-staging' : '';

  // Add WiX to PATH if not already present
  const wixPath = 'C:\\Program Files (x86)\\WiX Toolset v3.14\\bin';
  if (!process.env.PATH.includes(wixPath)) {
    process.env.PATH = `${process.env.PATH};${wixPath}`;
    console.log(`Added WiX to PATH: ${wixPath}`);
  }

  console.log(`Building MSI for ${isStaging ? 'STAGING' : 'PRODUCTION'} environment...`);

  // Find the built Electron app directory
  const APP_DIR = path.resolve(__dirname, 'release', 'win-unpacked');
  const OUT_DIR = path.resolve(__dirname, 'release', `msi${outDirSuffix}`);

  // Paths for icon embedding and MSI configuration
  const exePath = path.join(APP_DIR, 'PEI Site App.exe');
  const iconPath = path.resolve(__dirname, 'build', 'icon.ico');
  const rceditPath = path.join(__dirname, 'node_modules', 'rcedit', 'bin', 'rcedit-x64.exe');

  console.log('Embedding icon in executable...');
  try {
    if (fs.existsSync(exePath) && fs.existsSync(iconPath) && fs.existsSync(rceditPath)) {
      execSync(`"${rceditPath}" "${exePath}" --set-icon "${iconPath}" --set-version-string "ProductName" "PEI Site App${envSuffix}" --set-version-string "FileDescription" "PEI Site Application${envSuffix}" --set-version-string "CompanyName" "PEI Data Systems"`, {
        stdio: 'inherit'
      });
      console.log('Icon embedded successfully!');
    } else {
      console.log('Missing required files for icon embedding, skipping...');
    }
  } catch (error) {
    console.error('Failed to embed icon:', error.message);
    console.log('Continuing with MSI creation anyway...');
  }

  // Create MSI Creator
  const appName = `PEI Site App${envSuffix}`;
  const msiCreator = new MSICreator({
    appDirectory: APP_DIR,
    outputDirectory: OUT_DIR,

    // App metadata
    exe: 'PEI Site App',
    name: appName,
    manufacturer: 'PEI Data Systems',
    version: '1.0.0',
    description: `PEI Site Application${envSuffix}`,

    // Provide icon path to avoid native dependency issue
    appIconPath: path.resolve(__dirname, 'src', 'assets', 'icon.ico'),

    // Explicitly skip icon extraction to avoid native dependencies
    appUserModelId: 'com.pei.site-app',

    // MSI specific configuration
    upgradeCode: '57f48daa-3001-40a9-9dab-5a20450fd982', // Generate a new GUID for this

    // UI configuration
    ui: {
      chooseDirectory: true,
      template: `
        <UI Id="UserInterface">
          <TextStyle Id="WixUI_Font_Normal" FaceName="Tahoma" Size="8" />
          <TextStyle Id="WixUI_Font_Bigger" FaceName="Tahoma" Size="12" />
          <TextStyle Id="WixUI_Font_Title" FaceName="Tahoma" Size="9" Bold="yes" />
          <Property Id="DefaultUIFont" Value="WixUI_Font_Normal" />
          <Property Id="WixUI_Mode" Value="InstallDir" />
          <Property Id="WIXUI_INSTALLDIR" Value="APPLICATIONROOTDIRECTORY" />

          <DialogRef Id="BrowseDlg" />
          <DialogRef Id="DiskCostDlg" />
          <DialogRef Id="ErrorDlg" />
          <DialogRef Id="FatalError" />
          <DialogRef Id="FilesInUse" />
          <DialogRef Id="MsiRMFilesInUse" />
          <DialogRef Id="PrepareDlg" />
          <DialogRef Id="ProgressDlg" />
          <DialogRef Id="ResumeDlg" />
          <DialogRef Id="UserExit" />

          <!-- Custom Configuration Dialog -->
          <Dialog Id="ConfigurationDlg" Width="370" Height="310" Title="[ProductName] Configuration">
            <Control Id="Title" Type="Text" X="15" Y="6" Width="300" Height="15" Transparent="yes" NoPrefix="yes">
              <Text>{\\WixUI_Font_Title}Application Configuration</Text>
            </Control>

            <Control Id="Description" Type="Text" X="25" Y="23" Width="320" Height="30" Transparent="yes" NoPrefix="yes">
              <Text>Please enter your Tenant ID, Site ID, and Site Name. These values will be used to configure the application.</Text>
            </Control>

            <Control Id="BannerBitmap" Type="Bitmap" X="0" Y="0" Width="370" Height="44" TabSkip="no" Text="!(loc.InstallDirDlgBannerBitmap)" />
            <Control Id="BannerLine" Type="Line" X="0" Y="44" Width="370" Height="0" />

            <!-- Tenant ID Input -->
            <Control Id="TenantIdLabel" Type="Text" X="20" Y="60" Width="100" Height="15" TabSkip="no">
              <Text>&amp;Tenant ID:</Text>
            </Control>
            <Control Id="TenantIdEdit" Type="Edit" X="20" Y="75" Width="330" Height="18" Property="TENANT_ID" />

            <!-- Site ID Input -->
            <Control Id="SiteIdLabel" Type="Text" X="20" Y="100" Width="100" Height="15" TabSkip="no">
              <Text>&amp;Site ID:</Text>
            </Control>
            <Control Id="SiteIdEdit" Type="Edit" X="20" Y="115" Width="330" Height="18" Property="SITE_ID" />

            <!-- Site Name Input -->
            <Control Id="SiteNameLabel" Type="Text" X="20" Y="140" Width="100" Height="15" TabSkip="no">
              <Text>Site &amp;Name:</Text>
            </Control>
            <Control Id="SiteNameEdit" Type="Edit" X="20" Y="155" Width="330" Height="18" Property="SITE_NAME" />

            <!-- Start with Windows Checkbox -->
            <Control Id="StartWithWindowsCheckbox" Type="CheckBox" X="20" Y="185" Width="330" Height="17" Property="START_WITH_WINDOWS" CheckBoxValue="1">
              <Text>Start application automatically when Windows starts (recommended)</Text>
            </Control>

            <!-- Navigation buttons -->
            <Control Id="BottomLine" Type="Line" X="0" Y="274" Width="370" Height="0" />
            <Control Id="Back" Type="PushButton" X="180" Y="283" Width="56" Height="17" Text="!(loc.WixUIBack)" />
            <Control Id="Next" Type="PushButton" X="236" Y="283" Width="56" Height="17" Default="yes" Text="!(loc.WixUINext)" />
            <Control Id="Cancel" Type="PushButton" X="304" Y="283" Width="56" Height="17" Cancel="yes" Text="!(loc.WixUICancel)">
              <Publish Event="SpawnDialog" Value="CancelDlg">1</Publish>
            </Control>
          </Dialog>

          <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ConfigurationDlg">NOT Installed</Publish>
          <Publish Dialog="ConfigurationDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg">1</Publish>
          <Publish Dialog="ConfigurationDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg">1</Publish>
          <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="ConfigurationDlg">1</Publish>
          <Publish Dialog="InstallDirDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg">1</Publish>
          <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Property="_BrowseProperty" Value="[WIXUI_INSTALLDIR]" Order="1">1</Publish>
          <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Event="SpawnDialog" Value="BrowseDlg" Order="2">1</Publish>
          <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="InstallDirDlg" Order="1">NOT Installed</Publish>
          <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="MaintenanceTypeDlg" Order="2">Installed</Publish>
          <Publish Dialog="MaintenanceWelcomeDlg" Control="Next" Event="NewDialog" Value="MaintenanceTypeDlg">1</Publish>
          <Publish Dialog="MaintenanceTypeDlg" Control="RepairButton" Event="NewDialog" Value="VerifyReadyDlg">1</Publish>
          <Publish Dialog="MaintenanceTypeDlg" Control="RemoveButton" Event="NewDialog" Value="VerifyReadyDlg">1</Publish>
          <Publish Dialog="MaintenanceTypeDlg" Control="Back" Event="NewDialog" Value="MaintenanceWelcomeDlg">1</Publish>
          <Publish Dialog="ExitDialog" Control="Finish" Event="EndDialog" Value="Return" Order="999">1</Publish>
        </UI>
        <UIRef Id="WixUI_Common" />
      `,
      // images: {
      //   background: path.resolve(__dirname, 'installer', 'background.png'), // Optional: 493x312
      //   banner: path.resolve(__dirname, 'installer', 'banner.png'), // Optional: 493x58
      // }
    },

    // Custom features
    features: {
      autoLaunch: true,
      autoUpdate: false,
    },

    // WiX extension configuration for custom UI
    extensions: ['WixUtilExtension'],

    // Certificate configuration (optional, for signing)
    // signWithParams: '/a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256'
  });

  try {
    console.log('Creating MSI installer...');

    // Bypass icon extraction by providing appIconPath directly
    // This avoids the need for the native @bitdisaster/exe-icon-extractor module
    const iconPath = path.resolve(__dirname, 'src', 'assets', 'icon.ico');

    // Step 1: Create the .wxs file
    try {
      await msiCreator.create();
    } catch (error) {
      // If icon extraction fails, continue anyway - the MSI will still work
      if (error.message && error.message.includes('exe-icon-extractor')) {
        console.log('Icon extraction skipped (optional dependency missing)');
      } else {
        throw error;
      }
    }
    console.log('WiX source file created successfully');

    // Step 2: Modify the generated .wxs file to add custom properties and actions
    const fs = require('fs');
    // The library uses the exe name for the wxs file, not the full app name with suffix
    const wxsPath = path.join(OUT_DIR, 'PEI Site App.wxs');
    let wxsContent = fs.readFileSync(wxsPath, 'utf8');

    // Add custom properties for TENANT_ID, SITE_ID, and SITE_NAME after the Product opening tag
    const propertiesXml = `
    <!-- Custom properties to store user input -->
    <Property Id="TENANT_ID" Secure="yes">
      <RegistrySearch Id="TenantIdSearch"
                     Root="HKLM"
                     Key="Software\\PEI Data Systems\\[ProductName]"
                     Name="TenantId"
                     Type="raw" />
    </Property>

    <Property Id="SITE_ID" Secure="yes">
      <RegistrySearch Id="SiteIdSearch"
                     Root="HKLM"
                     Key="Software\\PEI Data Systems\\[ProductName]"
                     Name="SiteId"
                     Type="raw" />
    </Property>

    <Property Id="SITE_NAME" Secure="yes">
      <RegistrySearch Id="SiteNameSearch"
                     Root="HKLM"
                     Key="Software\\PEI Data Systems\\[ProductName]"
                     Name="SiteName"
                     Type="raw" />
    </Property>

    <!-- Property to control startup with Windows (default to yes) -->
    <Property Id="START_WITH_WINDOWS" Value="1" />

    <!-- Property for Add/Remove Programs icon -->
    <Property Id="ARPPRODUCTICON" Value="AppIcon.exe" />
`;

    // Find the first <Directory or <DirectoryRef tag and insert properties before it
    wxsContent = wxsContent.replace(/(\s+)(<Directory[\s>])/m, `$1${propertiesXml}$1$2`);

    // Add registry component as a new DirectoryRef section
    const registryComponentXml = `
    <DirectoryRef Id="APPLICATIONROOTDIRECTORY">
      <Component Id="ConfigRegistryEntries" Guid="*">
        <RegistryKey Root="HKLM" Key="Software\\PEI Data Systems\\[ProductName]" ForceCreateOnInstall="yes">
          <RegistryValue Type="string" Name="TenantId" Value="[TENANT_ID]" KeyPath="yes"/>
          <RegistryValue Type="string" Name="SiteId" Value="[SITE_ID]"/>
          <RegistryValue Type="string" Name="SiteName" Value="[SITE_NAME]"/>
          <RegistryValue Type="string" Name="InstallPath" Value="[APPLICATIONROOTDIRECTORY]"/>
        </RegistryKey>
      </Component>

      <!-- Component for Windows Startup - Conditional based on user choice -->
      <Component Id="StartupRegistryEntry" Guid="*">
        <Condition>START_WITH_WINDOWS = "1"</Condition>
        <RegistryKey Root="HKCU" Key="Software\\Microsoft\\Windows\\CurrentVersion\\Run">
          <RegistryValue Type="string" Name="[ProductName]" Value="&quot;[APPLICATIONROOTDIRECTORY]app-1.0.0\\PEI Site App.exe&quot;" KeyPath="yes"/>
        </RegistryKey>
      </Component>
    </DirectoryRef>
`;

    // Find an existing DirectoryRef and add our DirectoryRef after it
    wxsContent = wxsContent.replace(
      /(<DirectoryRef\s+Id="APPLICATIONROOTDIRECTORY">[\s\S]*?<\/DirectoryRef>)/,
      `$1\n${registryComponentXml}`
    );

    // Add ComponentRef to the main feature
    const componentRefXml = `
      <ComponentRef Id="ConfigRegistryEntries" />
      <ComponentRef Id="StartupRegistryEntry" />`;

    // Find the Feature element and add our component references
    wxsContent = wxsContent.replace(
      /(<Feature\s+Id="MainApplication"[^>]*>)/,
      `$1${componentRefXml}`
    );

    // Add icon attribute to shortcuts
    // For Start Menu shortcut
    wxsContent = wxsContent.replace(
      /(<Shortcut\s+Id="ApplicationStartMenuShortcut"[^>]*\n[^>]*\n[^>]*\n[^>]*)(WorkingDirectory="APPLICATIONROOTDIRECTORY">)/,
      `$1WorkingDirectory="APPLICATIONROOTDIRECTORY"\n                  Icon="AppIcon.exe"\n                  IconIndex="0">`
    );

    // For Desktop shortcut
    wxsContent = wxsContent.replace(
      /(<Shortcut\s+Id="MyDesktopShortcut"[^>]*\n[^>]*\n[^>]*\n[^>]*)(WorkingDirectory="APPLICATIONROOTDIRECTORY")/,
      `$1WorkingDirectory="APPLICATIONROOTDIRECTORY"\n                    Icon="AppIcon.exe"\n                    IconIndex="0"`
    );

    // Add Icon element definition before closing Product tag
    const iconDefinition = `
    <Icon Id="AppIcon.exe" SourceFile="${iconPath}" />
  </Product>`;

    wxsContent = wxsContent.replace(
      /<\/Product>/,
      iconDefinition
    );

    // Update the DisplayIcon registry value to explicitly include icon index
    wxsContent = wxsContent.replace(
      /(<RegistryValue\s+Name="DisplayIcon"\s+Type="expandable"\s+Value=")(\[APPLICATIONROOTDIRECTORY\]PEI Site App\.exe)(")/,
      '$1$2,0$3'
    );

    // Write the modified content back
    fs.writeFileSync(wxsPath, wxsContent, 'utf8');
    console.log('Modified WiX source file with custom properties, components, shortcut icons, and DisplayIcon');

    // Step 3: Compile the MSI
    await msiCreator.compile();
    console.log('MSI installer created successfully!');
    console.log(`Output location: ${OUT_DIR}`);

  } catch (error) {
    console.error('Error creating MSI:', error);
    process.exit(1);
  }
}

buildMSI();
