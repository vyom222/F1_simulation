// Driver number to surname mapping
const driverNumberToSurname = {
    1: 'Verstappen',
    2: 'Sargeant',
    3: 'Ricciardo',
    4: 'Norris',
    5: 'Bortoleto',
    6: 'Hadjar',
    7: 'Doohan',
    10: 'Gasly',
    11: 'Perez',
    12: 'Antonelli',
    14: 'Alonso',
    16: 'Leclerc',
    18: 'Stroll',
    20: 'Magnussen',
    21: 'de Vries',
    22: 'Tsunoda',
    23: 'Albon',
    24: 'Zhou',
    27: 'Hulkenberg',
    30: 'Lawson',
    31: 'Ocon',
    40: 'Lawson',
    43: 'Colapinto',
    44: 'Hamilton',
    55: 'Sainz',
    63: 'Russell',
    77: 'Bottas',
    81: 'Piastri',
    87: 'Bearman'
};

// Team name to colour mapping
const teamToColour = {
    'MERCEDES': '#40E0D0',
    'FERRARI': '#FF0000',
    'RED BULL RACING': '#003366',
    'RACING BULLS': '#5192d3',
    'MCLAREN': '#FF5900',
    'ALPINE': '#ff95e4',
    'ASTON MARTIN': '#00a86ac5',
    'WILLIAMS': '#7be0ff',
    'HAAS': '#9a9999',
    'ALFA ROMEO': '#8B0000',
    'SAUBER': '#1dff09',
    'ALPHATAURI': '#484848',
};

// Year-specific driver number to team mappings
const driverNumberToTeamByYear = {
    '2023': {
        1: 'Red Bull Racing', 11: 'Red Bull Racing',
        16: 'Ferrari', 55: 'Ferrari',
        44: 'Mercedes', 63: 'Mercedes',
        31: 'Alpine', 10: 'Alpine',
        4: 'McLaren', 81: 'McLaren',
        77: 'Alfa Romeo', 24: 'Alfa Romeo',
        18: 'Aston Martin', 14: 'Aston Martin',
        20: 'Haas', 27: 'Haas',
        3: 'AlphaTauri', 22: 'AlphaTauri',
        23: 'Williams', 2: 'Williams'
    },
    '2024': {
        1: 'Red Bull Racing', 11: 'Red Bull Racing',
        44: 'Mercedes', 63: 'Mercedes',
        16: 'Ferrari', 55: 'Ferrari',
        4: 'McLaren', 81: 'McLaren',
        14: 'Aston Martin', 18: 'Aston Martin',
        10: 'Alpine', 31: 'Alpine',
        27: 'Haas', 20: 'Haas',
        77: 'Sauber', 24: 'Sauber',
        3: 'Racing Bulls', 22: 'Racing Bulls',
        23: 'Williams', 2: 'Williams'
    },
    '2025': {
        4: 'McLaren', 81: 'McLaren',
        16: 'Ferrari', 44: 'Ferrari',
        1: 'Red Bull Racing', 30: 'Red Bull Racing',
        63: 'Mercedes', 12: 'Mercedes',
        14: 'Aston Martin', 18: 'Aston Martin',
        10: 'Alpine', 7: 'Alpine', 43: 'Alpine',
        31: 'Haas', 87: 'Haas',
        5: 'Sauber', 27: 'Sauber',
        22: 'Racing Bulls', 6: 'Racing Bulls',
        23: 'Williams', 55: 'Williams'
    }
};

// Check C# API status on page load
window.addEventListener('load', function() {
    // Check C# API health
    fetch('http://localhost:5000/api/solver/health')
        .then(response => response.json())
        .then(data => {
            const apiStatus = document.getElementById('apiStatus');
            apiStatus.textContent = data.service;
            apiStatus.className = 'status-message status-success';
        })
        .catch(error => {
            const apiStatus = document.getElementById('apiStatus');
            apiStatus.textContent = 'Could not connect to C# API. Run: dotnet run';
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
        
        const compound = curve.compound;
        const slope = curve.slope;
        const intercept = curve.intercept;
        
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

        const color = colors[compound];

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

async function loadStrats(){
    const country = document.getElementById('countrySelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadStratsBtn');
    const status = document.getElementById('stratStatus');
    const container = document.getElementById('strategiesContainer');

    if (!country || !year) {
        status.textContent = 'Select country & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading strategies...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/top-strategies?country=${country}&year=${year}`


    let lastError = null;

    console.log('Attempting strategies fetch:', url);

    // Abort previous requests so user can always start a new calculation
    if (window.currentStratController) {
        try { window.currentStratController.abort(); } catch(e) { console.warn('Could not abort previous controller', e); }
        window.currentStratController = null;
    }
    // Abortable fetch with timeout
    const controller = new AbortController();
    window.currentStratController = controller;
    const timeoutMs = 10000;
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

    try {
        const resp = await fetch(url, { signal: controller.signal, cache: 'no-store' });
        console.log('Strategies response status:', resp.status);
        const text = await resp.text();
        console.log('Raw response text length:', text ? text.length : 0);

        if (!text || !text.trim()) {
            throw new Error(`Empty response body (status ${resp.status})`);
        }

        let data;
        try {
            data = JSON.parse(text);
        } catch (e) {
            throw new Error(`Invalid JSON response: ${e.message} — body truncated: ${text.substring(0,200)}`);
        }

        if (!data.success || !data.strategies) {
            throw new Error(data.error || 'No strategies returned');
        }

        // Render strategies
        container.innerHTML = '';

        const raceLength = data.strategies.length > 0 && data.strategies[0].stints ? data.strategies[0].stints.reduce((acc, s) => acc + s.length, 0) : 66;

        data.strategies.forEach((s, idx) => {
            const row = document.createElement('div');
            row.className = 'strategy-row';

            // Left label (Strategy #)
            const label = document.createElement('div');
            label.className = 'strategy-label';
            label.textContent = `Strategy ${idx + 1}`;
            row.appendChild(label);

            // Bar wrapper
            const wrapper = document.createElement('div');
            wrapper.className = 'strategy-bar-wrapper';

            // Add lap ticks (every 6ish ticks)
            const ticks = document.createElement('div');
            ticks.className = 'lap-ticks';
            const tickInterval = Math.max(1, Math.ceil(raceLength / 6));
            for (let lap = 1; lap <= raceLength; lap += tickInterval) {
                const tick = document.createElement('span');
                tick.className = 'lap-tick';
                const leftPct = ((lap - 1) / raceLength) * 100;
                tick.style.left = leftPct + '%';
                tick.textContent = lap;
                ticks.appendChild(tick);
            }
            wrapper.appendChild(ticks);

            // Add stint segments
            s.stints.forEach(st => {
                const seg = document.createElement('div');
                seg.className = 'stint-segment';
                const pct = (st.length / raceLength) * 100;
                seg.style.width = pct + '%';
                seg.style.backgroundColor = (st.compound.toUpperCase().includes('SOFT')) ? '#FF0000'
                    : (st.compound.toUpperCase().includes('MEDIUM')) ? '#FFFF00' : '#888888';
                seg.title = `${st.compound} (${st.length} laps)`;
                seg.style.display = 'inline-block';
                seg.style.boxSizing = 'border-box';
                wrapper.appendChild(seg);
            });

            // Add pit window overlays (ensure they are on top)
            (s.windows || []).forEach(w => {
                console.log('Adding pit window for', w);
                const win = document.createElement('div');
                win.className = 'pit-window';

                const minLap = Number(w.min);
                const maxLap = Number(w.max);

                const leftPct = ((minLap - 1) / raceLength) * 100;
                const widthPct = ((maxLap - minLap + 1) / raceLength) * 100;

                if (widthPct <= 0) {
                    console.warn('Skipping pit window with non-positive width:', w, 'computed widthPct:', widthPct);
                    return;
                }

                win.style.left = leftPct + '%';
                win.style.width = widthPct + '%';

                // Force stacking and sizing to ensure visibility
                win.style.zIndex = '10';
                win.style.top = '0';
                win.style.height = '100%';

                // Add numeric labels inside the pit window (min and max)
                const leftLabelSpan = document.createElement('span');
                leftLabelSpan.className = 'pit-label left';
                leftLabelSpan.textContent = minLap;
                win.appendChild(leftLabelSpan);

                const rightLabelSpan = document.createElement('span');
                rightLabelSpan.className = 'pit-label right';
                rightLabelSpan.textContent = maxLap;
                win.appendChild(rightLabelSpan);

                wrapper.appendChild(win);
            });

            row.appendChild(wrapper);
            container.appendChild(row);
        });

        status.textContent = `Loaded top strategies (${data.strategies.length})`;
        status.className = 'status-message status-success';
        status.style.display = 'block';
        // Re-enable the button so the user can recalculate immediately
        button.disabled = false;
        if (window.currentStratController) { window.currentStratController = null; }
        clearTimeout(timeoutId);
        return;
        
    } catch (err) {
        console.error(`Error fetching strategies from ${url}:`, err);
        lastError = err;
        clearTimeout(timeoutId);
        if (window.currentStratController) { window.currentStratController = null; }

    }
    button.disabled = false;
}

function loadQuali() {
    const country = document.getElementById('countrySelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadQualifyingBtn');
    const status = document.getElementById('qualiStatus');

    if (!country || !year) {
        status.textContent = 'Select country & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading qualifying data...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/qualifying?country=${(country)}&year=${year}`;

    fetch(url)
        .then(response => response.json())
        .then(data => {
            const list = (data.qualifying && data.qualifying.qualifying) || [];
            if (!data.success || !Array.isArray(list) || list.length === 0) {
                status.textContent = data.error || 'No qualifying data available';
                status.className = 'status-message status-error';
                status.style.display = 'block';
                return;
            }
            plotQualifyingBarChart(list);
            status.textContent = `Loaded ${list.length} drivers`;
            status.className = 'status-message status-success';
            status.style.display = 'block';
        })
        .catch(err => {
            status.textContent = 'Error: ' + (err.message || err);
            status.className = 'status-message status-error';
            status.style.display = 'block';
        })
        .finally(() => {
            button.disabled = false;
        });
}

function parseGapSeconds(gapStr) {
    if (gapStr == null || gapStr === '') return 0;
    const s = String(gapStr).replace(/^\+/, '').trim();
    const n = parseFloat(s);
    return isNaN(n) ? 0 : n;
}

function plotQualifyingBarChart(qualifying) {
    const canvas = document.getElementById('qualifyingChart');
    if (!canvas) return;

    // Use a name that doesn't conflict with the canvas id (id="qualifyingChart" becomes window.qualifyingChart in browsers)
    if (window._qualifyingChartInstance && typeof window._qualifyingChartInstance.destroy === 'function') {
        window._qualifyingChartInstance.destroy();
        window._qualifyingChartInstance = null;
    }

    const labels = [];
    const gapSeconds = [];
    const backgroundColors = [];
    const borderColors = [];

    qualifying.forEach(d => {
        const pos = d.position ?? null;
        const driverNum = d.driver_number ?? '';
        const driverSurname = driverNumberToSurname[driverNum];
        labels.push(`P${pos ?? ''} ${driverSurname}`.trim());

        const gapValue = d.gap;
        gapSeconds.push(parseGapSeconds(gapValue));

        // Determine team name for that year
        const selectedYear = document.getElementById('yearSelect')?.value || '2024';
        const mappingForYear = driverNumberToTeamByYear[selectedYear];
        const teamFromMap = mappingForYear[Number(driverNum)];
        const possibleTeam = teamFromMap || '';
        const normTeam = String(possibleTeam).trim().toUpperCase();
        const colour = teamToColour[normTeam] || '#BDBDBD';
        let baseHex = String(colour).trim();
        backgroundColors.push(baseHex + 'CC');
        borderColors.push(baseHex);
    });

    const ctx = canvas.getContext('2d');
    window._qualifyingChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Gap to fastest (s)',
                data: gapSeconds,
                backgroundColor: backgroundColors,
                borderColor: borderColors,
                borderWidth: 1
            }]
        },
        options: {
            indexAxis: 'y',
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                title: { display: true, text: 'Qualifying - gap to fastest' }
            },
            scales: {
                x: {
                    title: { display: true, text: 'Gap (seconds)' },
                    grid: { display: true }
                },
                y: {
                    title: { display: false },
                    grid: { display: false },
                    ticks: {
                        autoSkip: false
                    }
                }
            }
        }
    });
}



function loadRacePace() {
    const country = document.getElementById('countrySelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadRacePaceBtn');
    const status = document.getElementById('racePaceStatus');

    if (!country || !year) {
        status.textContent = 'Select country & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading race pace data...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/race-pace?country=${(country)}&year=${year}`;

    fetch(url)
        .then(response => response.json())
        .then(data => {
            const raw = data.racePace || [];
            let list = [];
            if (Array.isArray(raw)) list = raw;
            else if (raw) list = raw.race_pace || [];

            if (!data.success || !Array.isArray(list) || list.length === 0) {
                status.textContent = data.error || 'No race pace data available';
                status.className = 'status-message status-error';
                status.style.display = 'block';
                return;
            }
            plotRacePaceBarChart(list);
            status.textContent = `Loaded ${list.length} drivers`;
            status.className = 'status-message status-success';
            status.style.display = 'block';
        })
        .catch(err => {
            status.textContent = 'Error: ' + (err.message || err);
            status.className = 'status-message status-error';
            status.style.display = 'block';
        })
        .finally(() => {
            button.disabled = false;
        });
}

/** Parse gap string to seconds - remove the '+' */
function parseGapSeconds(gapStr) {
    if (gapStr == null || gapStr === '') return 0;
    const s = String(gapStr).replace(/^\+/, '').trim();
    const n = parseFloat(s);
    return isNaN(n) ? 0 : n;
}

function plotRacePaceBarChart(racePace) {
    const canvas = document.getElementById('racePaceChart');
    if (!canvas) return;

    if (window._racePaceChartInstance && typeof window._racePaceChartInstance.destroy === 'function') {
        window._racePaceChartInstance.destroy();
        window._racePaceChartInstance = null;
    }

    const labels = [];
    const gapSeconds = [];
    const backgroundColors = [];
    const borderColors = [];

    racePace.forEach(d => {
        const pos = d.position ?? null;
        const driverNum = d.driver_number ?? '';
        const driverSurname = driverNumberToSurname[driverNum];
        labels.push(`P${pos ?? ''} ${driverSurname}`.trim());

        const gapValue = d.gap_to_fastest;
        gapSeconds.push(parseGapSeconds(gapValue));

        // Determine team name: select mapping for the currently selected year first
        const selectedYear = document.getElementById('yearSelect')?.value || '2024';
        const mappingForYear = driverNumberToTeamByYear[selectedYear];
        const teamFromMap = mappingForYear[Number(driverNum)];
        const possibleTeam = teamFromMap || '';
        const normTeam = String(possibleTeam).trim().toUpperCase();
        const colour = teamToColour[normTeam] || '#BDBDBD';
        let baseHex = String(colour).trim();
        backgroundColors.push(baseHex + 'CC');
        borderColors.push(baseHex);
    });

    const ctx = canvas.getContext('2d');
    window._racePaceChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Gap to fastest (s)',
                data: gapSeconds,
                backgroundColor: backgroundColors,
                borderColor: borderColors,
                borderWidth: 1
            }]
        },
        options: {
            indexAxis: 'y',
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                title: { display: true, text: 'Race Pace - gap to fastest' }
            },
            scales: {
                x: {
                    title: { display: true, text: 'Gap (seconds)' },
                    grid: { display: true }
                },
                y: {
                    title: { display: false },
                    grid: { display: false },
                    ticks: {
                        autoSkip: false
                    }
                }
            }
        }
    });
}