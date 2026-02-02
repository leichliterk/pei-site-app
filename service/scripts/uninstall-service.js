const Service = require('node-windows').Service;
const path = require('path');

// Get the path to the compiled service
const scriptPath = path.join(__dirname, '..', 'dist', 'index.js');

console.log('Uninstalling PEI Site Service...');

// Create a new service object
const svc = new Service({
  name: 'PEI Site Service',
  script: scriptPath
});

// Listen for the "uninstall" event
svc.on('uninstall', function() {
  console.log('Service uninstalled successfully!');
});

svc.on('stop', function() {
  console.log('Service stopped.');
});

svc.on('error', function(err) {
  console.error('Error:', err);
});

// Stop and uninstall the service
svc.stop();
setTimeout(() => {
  svc.uninstall();
}, 2000);
