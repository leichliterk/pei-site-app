const Service = require('node-windows').Service;
const path = require('path');

// Get the path to the compiled service
const scriptPath = path.join(__dirname, '..', 'dist', 'index.js');

console.log('Installing PEI Site Service...');
console.log('Script path:', scriptPath);

// Create a new service object
const svc = new Service({
  name: 'PEI Site Service',
  description: 'PEI Site App Background Service - Maintains WebSocket connection for industrial monitoring',
  script: scriptPath,
  nodeOptions: [],
  // Run as LocalSystem to start before user login
  // The service will have network access
  env: [{
    name: 'NODE_ENV',
    value: 'production'
  }]
});

// Listen for the "install" event
svc.on('install', function() {
  console.log('Service installed successfully!');
  console.log('Starting service...');
  svc.start();
});

svc.on('start', function() {
  console.log('Service started!');
  console.log('');
  console.log('The PEI Site Service is now running.');
  console.log('It will automatically start when Windows boots.');
});

svc.on('alreadyinstalled', function() {
  console.log('Service is already installed.');
});

svc.on('error', function(err) {
  console.error('Error:', err);
});

// Install the service
svc.install();
