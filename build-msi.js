const { MSICreator } = require('electron-wix-msi');
const path = require('path');

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

  // Create MSI Creator
  const msiCreator = new MSICreator({
    appDirectory: APP_DIR,
    outputDirectory: OUT_DIR,

    // App metadata
    exe: 'PEI Site App',
    name: `PEI Site App${envSuffix}`,
    manufacturer: 'PEI Data Systems',
    version: '1.0.0',
    description: `PEI Site Application${envSuffix}`,

    // Explicitly skip icon extraction to avoid native dependencies
    appUserModelId: 'com.pei.site-app',

    // MSI specific configuration
    upgradeCode: '57f48daa-3001-40a9-9dab-5a20450fd982', // Generate a new GUID for this

    // UI configuration
    ui: {
      chooseDirectory: true,
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

    // Custom WiX template
    wxsTemplate: path.resolve(__dirname, 'installer', 'msi-template.wxs'),

    // Certificate configuration (optional, for signing)
    // signWithParams: '/a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256'
  });

  try {
    console.log('Creating MSI installer...');

    // Step 1: Create the .wxs file
    await msiCreator.create();
    console.log('WiX source file created successfully');

    // Step 2: Compile the MSI
    await msiCreator.compile();
    console.log('MSI installer created successfully!');
    console.log(`Output location: ${OUT_DIR}`);

  } catch (error) {
    console.error('Error creating MSI:', error);
    process.exit(1);
  }
}

buildMSI();
