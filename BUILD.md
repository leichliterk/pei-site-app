# Build Instructions

## Development

Start the development server:
```bash
npm start
```
Runs on `http://localhost:4300`

## Electron Development

Run Electron in development mode:
```bash
npm run electron-dev
```

## Production Builds

### Build MSI Installer (Production)

Uses `environment.ts` with development/default settings:

```bash
npm run electron:build:msi
```

Output: `release/msi/PEI Site App.msi`

### Build MSI Installer (Staging)

Uses `environment.staging.ts` with staging API URL:
- API URL: `https://pei-web-server-staging.onrender.com/api/data`
- Site Name: "Staging Site"

```bash
npm run electron:build:msi:staging
```

Output: `release/msi-staging/PEI Site App (Staging).msi`

## Environment Files

- **environment.ts** - Development/Default
  - API: `http://localhost:443/api/data`

- **environment.staging.ts** - Staging
  - API: `https://pei-web-server-staging.onrender.com/api/data`

- **environment.prod.ts** - Production (not currently used for builds)

## Build Process

1. **Angular Build** - Compiles Angular app with environment replacement
2. **Electron Build** - Compiles TypeScript Electron main process
3. **Electron Builder** - Packages app into Windows executable
4. **MSI Creator** - Creates Windows installer with WiX toolset

## Requirements

- Node.js & npm
- WiX Toolset v3.14 (for MSI creation)
  - Default path: `C:\Program Files (x86)\WiX Toolset v3.14\bin`
