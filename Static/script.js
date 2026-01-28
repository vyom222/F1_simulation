// Check C# API status on page load
window.addEventListener('load', function() {
    // Check C# API health
    fetch('http://localhost:5000/api/solver/health')
        .then(response => response.json())
        .then(data => {
            const apiStatus = document.getElementById('apiStatus');
            apiStatus.textContent = '✓ ' + data.service;
            apiStatus.className = 'status-message status-success';
        })
        .catch(error => {
            const apiStatus = document.getElementById('apiStatus');
            apiStatus.textContent = '✗ Could not connect to C# API. Make sure to run: dotnet run';
            apiStatus.className = 'status-message status-error';
        });

    // Load curves on page load
    setTimeout(() => {
        loadTyreCurves();
    }, 1000);
});

function runCSharp() {
    const button = document.getElementById('runButton');
    const status = document.getElementById('csharpStatus');
    const output = document.getElementById('output');

    // Disable button and show loading
    button.disabled = true;
    status.textContent = 'Running F1 Simulation Solver...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';
    output.style.display = 'none';

    fetch('http://localhost:5000/api/solver/run-solver?country=Spain&year=2024')
        .then(response => response.json())
        .then(data => {
            // Update status
            if (data.success) {
                status.textContent = 'Simulation completed successfully';
                status.className = 'status-message status-success';
            } else {
                status.textContent = 'Simulation encountered errors';
                status.className = 'status-message status-error';
            }

            // Display output
            output.textContent = data.output || 'No output';
            output.style.display = 'block';

            // Re-enable button
            button.disabled = false;
        })
        .catch(error => {
            status.textContent = 'Error: ' + error;
            status.className = 'status-message status-error';
            output.textContent = 'Failed to connect to C# API. Make sure to run: dotnet run';
            output.style.display = 'block';
            button.disabled = false;
        });
}

function loadTyreCurves() {
    const country = document.getElementById('countrySelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadCurvesBtn');

    console.log('Loading curves for:', country, year);

    if (!country || !year) {
        showStatus('Please select both country and year', 'error');
        return;
    }

    button.disabled = true;
    showStatus('Loading tyre curves...', 'loading');

    const url = `http://localhost:5000/api/solver/tyre-curves?country=${country}&year=${year}`;
    console.log('Fetching from:', url);

    fetch(url)
        .then(response => {
            console.log('Response status:', response.status);
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .then(data => {
            console.log('Received data:', data);
            if (data.success && data.curves && data.curves.length > 0) {
                console.log('Curves count:', data.curves.length);
                console.log('First curve:', data.curves[0]);
                plotTyreCurves(data.curves);
                showStatus('Tyre curves loaded successfully', 'success');
            } else if (data.error) {
                console.error('API error:', data.error);
                showStatus(`Error: ${data.error}`, 'error');
            } else {
                console.error('Invalid data structure:', data);
                showStatus('Failed to load tyre curves - no data received', 'error');
            }
        })
        .catch(error => {
            console.error('Fetch error:', error);
            showStatus(`Error: ${error.message}`, 'error');
        })
        .finally(() => {
            button.disabled = false;
        });
}

function showStatus(message, type) {
    const status = document.getElementById('curveStatus');
    status.textContent = message;
    status.className = 'status-message status-' + type;
    status.style.display = 'block';
}

function plotTyreCurves(curves) {
    console.log('plotTyreCurves called with:', curves);
    
    const canvas = document.getElementById('tyreCurvesChart');
    if (!canvas) {
        console.error('Canvas element not found');
        showStatus('Error: Canvas element not found', 'error');
        return;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) {
        console.error('Could not get canvas context');
        showStatus('Error: Could not get canvas context', 'error');
        return;
    }

    if (!curves || curves.length === 0) {
        console.error('No curves data provided');
        showStatus('Error: No curves data provided', 'error');
        return;
    }

    // Define colors for each compound
    const colors = {
        'SOFT': { borderColor: '#FF0000', backgroundColor: 'rgba(255, 0, 0, 0.1)' },
        'MEDIUM': { borderColor: '#FFFF00', backgroundColor: 'rgba(255, 255, 0, 0.1)' },
        'HARD': { borderColor: '#49536135', backgroundColor: 'rgba(255, 255, 255, 0.1)' }
    };

    // Prepare datasets
    const datasets = [];
    for (const curve of curves) {
        console.log('Processing curve:', curve);
        
        const compound = curve.compound || curve.Compound;
        const slope = curve.slope || curve.Slope;
        const intercept = curve.intercept || curve.Intercept;
        
        console.log(`Compound: ${compound}, Slope: ${slope}, Intercept: ${intercept}`);
        
        // Generate curve points using y = mx + c formula
        const curveY = [];
        for (let lap = 0; lap <= 31; lap++) {
            curveY.push(lap * slope + intercept);
        }
        
        console.log(`Generated ${curveY.length} points for ${compound}`);

        if (!compound || slope === undefined || intercept === undefined) {
            console.warn('Skipping invalid curve - missing required fields:', curve);
            continue;
        }

        const color = colors[compound] || { borderColor: '#000000', backgroundColor: 'rgba(0, 0, 0, 0.1)' };

        datasets.push({
            label: compound,
            data: curveY,
            borderColor: color.borderColor,
            backgroundColor: color.backgroundColor,
            borderWidth: 3,
            fill: false,
            tension: 0.1,
            pointRadius: 0,
            pointHoverRadius: 6
        });
    }

    if (datasets.length === 0) {
        console.error('No valid datasets created');
        showStatus('Error: No valid datasets created from curves', 'error');
        return;
    }

    console.log('Datasets to plot:', datasets);

    // Destroy existing chart if it exists
    if (window.tyreCurveChart) {
        console.log('Destroying existing chart');
        window.tyreCurveChart.destroy();
    }

    // Create new chart
    try {
        window.tyreCurveChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: Array.from({length: 30}, (_, i) => i),
                datasets: datasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: true,
                        position: 'top'
                    },
                    title: {
                        display: true,
                        text: 'Tyre Degradation Curves'
                    }
                },
                scales: {
                    x: {
                        title: {
                            display: true,
                            text: 'Lap Number'
                        }
                    },
                    y: {
                        title: {
                            display: true,
                            text: 'Lap Time (seconds)'
                        }
                    }
                }
            }
        });
        console.log('Chart created successfully');
    } catch (e) {
        console.error('Error creating chart:', e);
        showStatus(`Error creating chart: ${e.message}`, 'error');
    }
}
