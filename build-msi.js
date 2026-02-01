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

    // UI configuration - use WixUI_InstallDir as base
    ui: {
      chooseDirectory: true
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
                     Key="Software\\PEI Data Systems\\PEI Site App"
                     Name="TenantId"
                     Type="raw" />
    </Property>

    <Property Id="SITE_ID" Secure="yes">
      <RegistrySearch Id="SiteIdSearch"
                     Root="HKLM"
                     Key="Software\\PEI Data Systems\\PEI Site App"
                     Name="SiteId"
                     Type="raw" />
    </Property>

    <Property Id="SITE_NAME" Secure="yes">
      <RegistrySearch Id="SiteNameSearch"
                     Root="HKLM"
                     Key="Software\\PEI Data Systems\\PEI Site App"
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
    // 32-bit MSI writes to WOW6432Node on 64-bit Windows - Electron app handles this
    const registryComponentXml = `
    <DirectoryRef Id="APPLICATIONROOTDIRECTORY">
      <Component Id="ConfigRegistryEntries" Guid="*">
        <RegistryKey Root="HKLM" Key="Software\\PEI Data Systems\\PEI Site App" ForceCreateOnInstall="yes">
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

      <!-- Install icon file for Add/Remove Programs display -->
      <Component Id="AppIconFile" Guid="*">
        <File Id="AppIconIco" Name="app-icon.ico" Source="${iconPath}" KeyPath="yes" />
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
      <ComponentRef Id="StartupRegistryEntry" />
      <ComponentRef Id="AppIconFile" />`;

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

    // Update the DisplayIcon registry value to point to the installed .ico file
    // This ensures Windows Settings shows the correct icon
    wxsContent = wxsContent.replace(
      /(<RegistryValue\s+Name="DisplayIcon"\s+Type="expandable"\s+Value=")(\[APPLICATIONROOTDIRECTORY\]PEI Site App\.exe)(")/,
      '$1[APPLICATIONROOTDIRECTORY]app-icon.ico$3'
    );

    // Fix the product name in Windows Settings - remove "(Machine)" suffix
    // Override VisibleProductName to just be "PEI Site App"
    wxsContent = wxsContent.replace(
      /<Property Id="VisibleProductName" Value="[^"]*"/,
      '<Property Id="VisibleProductName" Value="PEI Site App"'
    );

    // Remove the SetProperty actions that add "(User)" suffix
    // These span multiple lines and contain CDATA sections
    wxsContent = wxsContent.replace(
      /<!--[^>]*change the product name[^>]*-->\s*<SetProperty Action="SetVisibleProductName"[\s\S]*?<\/SetProperty>/g,
      ''
    );
    wxsContent = wxsContent.replace(
      /<!--[^>]*MSI generaten entry[^>]*-->\s*<SetProperty Action="SetProductName"[\s\S]*?<\/SetProperty>/g,
      ''
    );

    // Remove electron-wix-msi's default RegistryRunKey component to prevent duplicate startup entries
    // We have our own conditional StartupRegistryEntry component
    wxsContent = wxsContent.replace(
      /<Component Id="RegistryRunKey"[\s\S]*?<\/Component>/g,
      ''
    );

    // Remove the AutoLaunch feature that references the removed RegistryRunKey
    wxsContent = wxsContent.replace(
      /<Feature Id="AutoLaunch"[\s\S]*?<\/Feature>/g,
      ''
    );

    // Completely replace the UI section with a custom one
    // Using WixUI_Common as base and defining our own complete dialog flow
    const customUiXml = `
    <UI Id="CustomInstallUI">
      <UIRef Id="WixUI_Common" />
      <Property Id="WIXUI_INSTALLDIR" Value="APPLICATIONROOTDIRECTORY" />

      <TextStyle Id="WixUI_Font_Normal" FaceName="Tahoma" Size="8" />
      <TextStyle Id="WixUI_Font_Bigger" FaceName="Tahoma" Size="12" />
      <TextStyle Id="WixUI_Font_Title" FaceName="Tahoma" Size="9" Bold="yes" />
      <Property Id="DefaultUIFont" Value="WixUI_Font_Normal" />

      <!-- Standard dialog references -->
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
      <DialogRef Id="WelcomeDlg" />
      <DialogRef Id="InstallDirDlg" />
      <DialogRef Id="InvalidDirDlg" />
      <DialogRef Id="VerifyReadyDlg" />
      <DialogRef Id="ExitDialog" />

      <!-- Custom Configuration Dialog -->
      <Dialog Id="ConfigurationDlg" Width="370" Height="270" Title="[ProductName] Setup">
        <Control Id="BannerBitmap" Type="Bitmap" X="0" Y="0" Width="370" Height="44" TabSkip="no" Text="!(loc.InstallDirDlgBannerBitmap)" />
        <Control Id="BannerLine" Type="Line" X="0" Y="44" Width="370" Height="0" />
        <Control Id="BottomLine" Type="Line" X="0" Y="234" Width="370" Height="0" />

        <Control Id="Title" Type="Text" X="15" Y="6" Width="200" Height="15" Transparent="yes" NoPrefix="yes" Text="{\\WixUI_Font_Title}Site Configuration" />
        <Control Id="Description" Type="Text" X="25" Y="23" Width="280" Height="15" Transparent="yes" NoPrefix="yes" Text="Please enter your site configuration details." />

        <!-- Tenant ID Input -->
        <Control Id="TenantIdLabel" Type="Text" X="20" Y="55" Width="100" Height="15" NoPrefix="yes" Text="Tenant ID:" />
        <Control Id="TenantIdEdit" Type="Edit" X="20" Y="70" Width="330" Height="18" Property="TENANT_ID" />

        <!-- Site ID Input -->
        <Control Id="SiteIdLabel" Type="Text" X="20" Y="95" Width="100" Height="15" NoPrefix="yes" Text="Site ID:" />
        <Control Id="SiteIdEdit" Type="Edit" X="20" Y="110" Width="330" Height="18" Property="SITE_ID" />

        <!-- Site Name Input -->
        <Control Id="SiteNameLabel" Type="Text" X="20" Y="135" Width="100" Height="15" NoPrefix="yes" Text="Site Name:" />
        <Control Id="SiteNameEdit" Type="Edit" X="20" Y="150" Width="330" Height="18" Property="SITE_NAME" />

        <!-- Start with Windows Checkbox -->
        <Control Id="StartWithWindowsCheckbox" Type="CheckBox" X="20" Y="180" Width="330" Height="17" Property="START_WITH_WINDOWS" CheckBoxValue="1" Text="Start application automatically when Windows starts" />

        <!-- Navigation buttons -->
        <Control Id="Back" Type="PushButton" X="180" Y="243" Width="56" Height="17" Text="!(loc.WixUIBack)" />
        <Control Id="Next" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="!(loc.WixUINext)" />
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="!(loc.WixUICancel)">
          <Publish Event="SpawnDialog" Value="CancelDlg">1</Publish>
        </Control>
      </Dialog>

      <!-- Complete dialog flow for fresh install: Welcome -> Configuration -> InstallDir -> VerifyReady -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ConfigurationDlg">NOT Installed</Publish>
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg">Installed AND PATCH</Publish>

      <Publish Dialog="ConfigurationDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg">1</Publish>
      <Publish Dialog="ConfigurationDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg">1</Publish>

      <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="ConfigurationDlg">1</Publish>
      <Publish Dialog="InstallDirDlg" Control="Next" Event="SetTargetPath" Value="[WIXUI_INSTALLDIR]" Order="1">1</Publish>
      <Publish Dialog="InstallDirDlg" Control="Next" Event="DoAction" Value="WixUIValidatePath" Order="2">NOT WIXUI_DONTVALIDATEPATH</Publish>
      <Publish Dialog="InstallDirDlg" Control="Next" Event="SpawnDialog" Value="InvalidDirDlg" Order="3"><![CDATA[NOT WIXUI_DONTVALIDATEPATH AND WIXUI_INSTALLDIR_VALID<>"1"]]></Publish>
      <Publish Dialog="InstallDirDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg" Order="4">WIXUI_DONTVALIDATEPATH OR WIXUI_INSTALLDIR_VALID="1"</Publish>
      <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Property="_BrowseProperty" Value="[WIXUI_INSTALLDIR]" Order="1">1</Publish>
      <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Event="SpawnDialog" Value="BrowseDlg" Order="2">1</Publish>

      <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="InstallDirDlg" Order="1">NOT Installed</Publish>
      <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg" Order="2">Installed AND PATCH</Publish>

      <Publish Dialog="ExitDialog" Control="Finish" Event="EndDialog" Value="Return" Order="999">1</Publish>
    </UI>
    <UIRef Id="WixUI_ErrorProgressText" />
`;

    // Replace the entire UI section with our custom one
    // Match UI sections with or without Id attribute, and also remove the following UIRef if present
    const uiSectionRegex = /<UI[^>]*>[\s\S]*?<\/UI>\s*(?:<UIRef[^>]*\/>)?/;
    if (uiSectionRegex.test(wxsContent)) {
      wxsContent = wxsContent.replace(uiSectionRegex, customUiXml);
      console.log('Replaced UI section with custom UI');
    } else {
      console.error('Could not find UI section in WXS file');
    }

    // Write the modified content back
    fs.writeFileSync(wxsPath, wxsContent, 'utf8');
    console.log('Modified WiX source file with custom UI, properties, components, and icons');

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
