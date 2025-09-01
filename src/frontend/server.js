const express = require('express');
const app = express();
const port = 3000;

// Health check endpoint
app.get('/health', (req, res) => {
  res.json({ status: 'Healthy', service: 'Frontend' });
});

// Endpoint to check backend health via Dapr
app.get('/check-backend-health', async (req, res) => {
  try {
    // Using Dapr service invocation to call backend health endpoint
    const response = await fetch('http://localhost:3500/v1.0/invoke/aihub-backend/method/v1/health');
    const data = await response.text();
    res.json({ 
      status: 'Backend Health Check Successful', 
      backendResponse: data 
    });
  } catch (error) {
    res.status(500).json({ 
      status: 'Backend Health Check Failed', 
      error: error.message 
    });
  }
});

// Endpoint to check worker health via Dapr
app.get('/check-worker-health', async (req, res) => {
  try {
    // Using Dapr service invocation to call worker health endpoint
    const response = await fetch('http://localhost:3500/v1.0/invoke/aihub-worker/method/v1/health');
    const data = await response.text();
    res.json({ 
      status: 'Worker Health Check Successful', 
      workerResponse: data 
    });
  } catch (error) {
    res.status(500).json({ 
      status: 'Worker Health Check Failed', 
      error: error.message 
    });
  }
});

// Main page
app.get('/', (req, res) => {
  res.send(`
    <h1>AI Hub - System Status</h1>
    <div>
      <h2>Backend API Status</h2>
      <button onclick="checkBackendHealth()">Check Backend Health</button>
      <div id="backend-status"></div>
    </div>
    <div>
      <h2>Worker Service Status</h2>
      <button onclick="checkWorkerHealth()">Check Worker Health</button>
      <div id="worker-status"></div>
    </div>
    <script>
      async function checkBackendHealth() {
        const response = await fetch('/check-backend-health');
        const data = await response.json();
        document.getElementById('backend-status').innerHTML = '<pre>' + JSON.stringify(data, null, 2) + '</pre>';
      }
      
      async function checkWorkerHealth() {
        const response = await fetch('/check-worker-health');
        const data = await response.json();
        document.getElementById('worker-status').innerHTML = '<pre>' + JSON.stringify(data, null, 2) + '</pre>';
      }
    </script>
  `);
});

app.listen(port, () => {
  console.log('Frontend server running at http://localhost:' + port);
});