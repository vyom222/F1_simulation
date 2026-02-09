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

// Run everything on page load
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
        loadStrats();
        loadQuali();
        loadRacePace();
        loadRaceSimulation();
        loadMonteCarlo();
    }, 1000);
});

function runAll() {
    loadTyreCurves();
    loadStrats();
    loadQuali();
    loadRacePace();
    loadRaceSimulation();
    loadMonteCarlo();
}

function loadTyreCurves() {
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadCurvesBtn');

    console.log('Loading curves for:', circuit, year);

    if (!circuit || !year) {
        showStatus('Please select both circuit and year', 'error');
        return;
    }

    button.disabled = true;
    showStatus('Loading tyre curves...', 'loading');

    const url = `http://localhost:5000/api/solver/tyre-curves?circuit=${circuit}&year=${year}`;
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
                // Cache the tyre curves globally
                globalTyreCurves = data.curves;
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
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadStratsBtn');
    const status = document.getElementById('stratStatus');
    const container = document.getElementById('strategiesContainer');

    if (!circuit || !year) {
        status.textContent = 'Select circuit & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading strategies...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/top-strategies?circuit=${circuit}&year=${year}`


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

        // Cache the best strategy globally
        if (data.strategies.length > 0) {
            globalBestStrategy = data.strategies[0];
        }

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
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadQualifyingBtn');
    const status = document.getElementById('qualiStatus');

    if (!circuit || !year) {
        status.textContent = 'Select circuit & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading qualifying data...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/qualifying?circuit=${circuit}&year=${year}`;

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
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const button = document.getElementById('loadRacePaceBtn');
    const status = document.getElementById('racePaceStatus');

    if (!circuit || !year) {
        status.textContent = 'Select circuit & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    button.disabled = true;
    status.textContent = 'Loading race pace data...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    const url = `http://localhost:5000/api/solver/race-pace?circuit=${circuit}&year=${year}`;

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

// Load race simulation results
async function loadRaceSimulation() {
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const status = document.getElementById('raceSimStatus');
    const btn = document.getElementById('loadRaceSimBtn');

    if (!circuit || !year) {
        status.textContent = 'Select circuit & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    btn.disabled = true;
    status.textContent = 'Running race simulation...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    try {
        const url = `http://localhost:5000/api/solver/race-simulation?circuit=${circuit}&year=${year}&raceLength=66`;
        const resp = await fetch(url);
        const data = await resp.json();
        if (!data.success) throw new Error(data.error || 'Race simulation failed');

        // Populate race results table
        if (data.raceResults && data.raceResults.length > 0) {
            populateRaceResultsTable(data.raceResults);
            status.textContent = 'Race simulation complete';
            status.className = 'status-message status-success';
        } else {
            throw new Error('No race results returned');
        }
    } catch (err) {
        console.error('Race simulation error', err);
        status.textContent = 'Error: ' + (err.message || err);
        status.className = 'status-message status-error';
    } finally {
        btn.disabled = false;
        status.style.display = 'block';
    }
}

// Load Monte Carlo distribution and render chart
async function loadMonteCarlo() {
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    const sims = Number(document.getElementById('mcSims').value) || 500;
    const status = document.getElementById('monteStatus');
    const btn = document.getElementById('loadMonteBtn');

    if (!circuit || !year) {
        status.textContent = 'Select circuit & year';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }

    btn.disabled = true;
    status.textContent = 'Running Monte Carlo...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';

    try {
        const url = `http://localhost:5000/api/solver/montecarlo?circuit=${circuit}&year=${year}&numSimulations=${sims}`;
        const resp = await fetch(url);
        const data = await resp.json();
        if (!data.success) throw new Error(data.error || 'Monte Carlo failed');

        const avg = data.averagePositions || {};
        const counts = data.positionCounts || {};

        // Populate driver select
        const select = document.getElementById('mcDriverSelect');
        select.innerHTML = '';
        const driverNums = Object.keys(avg).map(k => Number(k)).sort((a,b)=>a-b);
        driverNums.forEach(dn => {
            const opt = document.createElement('option');
            const name = driverNumberToSurname[dn] || dn;
            opt.value = dn;
            opt.textContent = `${name} (${dn})`;
            select.appendChild(opt);
        });

        // When selection changes, replot
        select.onchange = () => plotMonteCarloDistribution(avg, counts);

        // show expected for first
        plotMonteCarloDistribution(avg, counts);

        // Populate standings table
        populateStandingsTable(avg);

        status.textContent = `Monte Carlo loaded (${data.simulations || sims} sims)`;
        status.className = 'status-message status-success';
        status.style.display = 'block';
    } catch (err) {
        console.error('Monte Carlo error', err);
        status.textContent = 'Error: ' + (err.message || err);
        status.className = 'status-message status-error';
        status.style.display = 'block';
    } finally {
        btn.disabled = false;
    }
}

function plotMonteCarloDistribution(averagePositions, positionCounts) {
    const select = document.getElementById('mcDriverSelect');
    if (!select || !select.value) return;
    const driverNum = Number(select.value);
    const dist = positionCounts[driverNum] || {};

    // Build labels (positions) sorted
    const positions = Object.keys(dist).map(k => Number(k)).sort((a,b)=>a-b);
    const counts = positions.map(p => dist[p] || 0);
    const total = counts.reduce((a,b)=>a+b, 0) || 1;
    const percentages = counts.map(c => (c/total*100));

    // Update expected position display
    const expected = averagePositions[driverNum];
    const expEl = document.getElementById('mcExpected');
    expEl.textContent = expected ? expected.toFixed(2) : '-';

    // Compute median position
    let medianPos = '-';
    if (positions.length > 0) {
        let cum = 0;
        for (let i = 0; i < positions.length; i++) {
            cum += dist[positions[i]];
            if (cum / total >= 0.5) { medianPos = positions[i]; break; }
        }
    }
    const medianEl = document.getElementById('mcMedian');
    medianEl.textContent = medianPos === '-' ? '-' : `P${medianPos}`;

    // mode
    let modePos = '-';
    if (positions.length > 0) {
        let maxCount = -1;
        for (const p of positions) {
            const c = dist[p] || 0;
            if (c > maxCount) { maxCount = c; modePos = p; }
        }
    }
    const modeEl = document.getElementById('mcMode');
    modeEl.textContent = modePos === '-' ? '-' : `P${modePos}`;

    // Compute expected points
    function pointsForPosition(pos) {
        const pts = [25,18,15,12,10,8,6,4,2,1];
        return pos >= 1 && pos <= 10 ? pts[pos-1] : 0;
    }
    let expectedPoints = 0;
    for (let i = 0; i < positions.length; i++) {
        const p = positions[i];
        const c = dist[p] || 0;
        expectedPoints += (c / total) * pointsForPosition(p);
    }
    const pointsEl = document.getElementById('mcPoints');
    pointsEl.textContent = expectedPoints >= 0 ? expectedPoints.toFixed(2) : '-';

    // Prepare chart
    const canvas = document.getElementById('monteCarloChart');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');

    if (window._monteChart && typeof window._monteChart.destroy === 'function') {
        window._monteChart.destroy();
        window._monteChart = null;
    }

    window._monteChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: positions.map(p => `P${p}`),
            datasets: [{
                label: 'Finish %',
                data: percentages,
                backgroundColor: '#667eeaCC',
                borderColor: '#667eea',
                borderWidth: 1
            }]
        },
        options: {
            indexAxis: 'y',
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                title: { display: true, text: `Position Distribution for ${driverNumberToSurname[driverNum] || driverNum}` }
            },
            scales: {
                x: {
                    title: { display: true, text: 'Percentage (%)' },
                    ticks: { callback: v => v.toFixed ? v.toFixed(1) + '%' : v }
                },
                y: {
                    title: { display: false },
                    ticks: {
                        autoSkip: false
                    }
                }
            }
        }
    });
}

// Populate race results table
function populateRaceResultsTable(raceResults) {
    const container = document.getElementById('raceResultsContainer');
    const emptyMsg = document.getElementById('raceResultsEmpty');
    const tbody = document.getElementById('raceResultsBody');

    if (!raceResults || raceResults.length === 0) {
        container.style.display = 'none';
        emptyMsg.style.display = 'block';
        return;
    }

    // Show container and hide empty message
    container.style.display = 'block';
    emptyMsg.style.display = 'none';

    // Clear existing rows
    tbody.innerHTML = '';

    // Populate table rows
    raceResults.forEach((result, index) => {
        const row = document.createElement('tr');
        
        // Alternate row colors
        if (index % 2 === 0) {
            row.style.backgroundColor = '#f9f9f9';
        }

        // Position
        const posCell = document.createElement('td');
        posCell.textContent = result.position;
        posCell.style.padding = '10px 12px';
        posCell.style.border = '1px solid #ddd';
        posCell.style.fontWeight = 'bold';
        row.appendChild(posCell);

        // Driver
        const driverCell = document.createElement('td');
        const driverName = driverNumberToSurname[result.driverNumber] || `Driver ${result.driverNumber}`;
        driverCell.textContent = `${driverName} (#${result.driverNumber})`;
        driverCell.style.padding = '10px 12px';
        driverCell.style.border = '1px solid #ddd';
        row.appendChild(driverCell);

        // Strategy
        const stratCell = document.createElement('td');
        stratCell.textContent = result.strategy;
        stratCell.style.padding = '10px 12px';
        stratCell.style.border = '1px solid #ddd';
        stratCell.style.fontFamily = 'monospace';
        stratCell.style.fontWeight = 'bold';
        row.appendChild(stratCell);

        // Total Time
        const timeCell = document.createElement('td');
        timeCell.textContent = formatTime(result.totalTime);
        timeCell.style.padding = '10px 12px';
        timeCell.style.border = '1px solid #ddd';
        timeCell.style.textAlign = 'right';
        timeCell.style.fontFamily = 'monospace';
        row.appendChild(timeCell);

        // Delta to First
        const deltaCell = document.createElement('td');
        if (result.position === 1) {
            deltaCell.textContent = '-';
        } else {
            deltaCell.textContent = '+' + result.deltaToFirst.toFixed(3) + 's';
        }
        deltaCell.style.padding = '10px 12px';
        deltaCell.style.border = '1px solid #ddd';
        deltaCell.style.textAlign = 'right';
        deltaCell.style.fontFamily = 'monospace';
        row.appendChild(deltaCell);

        tbody.appendChild(row);
    });
}

// Helper function to format time in seconds to mm:ss.sss
function formatTime(seconds) {
    const hours = Math.floor(seconds/3600);
    const minutes = Math.floor((seconds-hours*3600) / 60);
    const secs = seconds % 60;
    return `${hours}:${minutes}:${secs.toFixed(3).padStart(6, '0')}`;
}

// Populate Monte Carlo expected standings table
function populateStandingsTable(averagePositions) {
    const container = document.getElementById('standingsContainer');
    const emptyMsg = document.getElementById('standingsEmpty');
    const tbody = document.getElementById('standingsBody');

    if (!averagePositions || Object.keys(averagePositions).length === 0) {
        container.style.display = 'none';
        emptyMsg.style.display = 'block';
        return;
    }

    // Show container and hide empty message
    container.style.display = 'block';
    emptyMsg.style.display = 'none';

    // Clear existing rows
    tbody.innerHTML = '';

    // Sort drivers by expected position
    const sortedDrivers = Object.entries(averagePositions)
        .map(([driverNum, avgPos]) => ({ driverNum: Number(driverNum), avgPos }))
        .sort((a, b) => a.avgPos - b.avgPos);

    // Helper function to calculate expected points
    function pointsForPosition(pos) {
        const pts = [25, 18, 15, 12, 10, 8, 6, 4, 2, 1];
        return pos >= 1 && pos <= 10 ? pts[pos - 1] : 0;
    }

    sortedDrivers.forEach((driver, index) => {
        const row = document.createElement('tr');
        
        // Alternate row colors
        if (index % 2 === 0) {
            row.style.backgroundColor = '#f9f9f9';
        }

        // Highlight top 3
        if (index === 0) {
            row.style.backgroundColor = '#ffd700'; // Gold
            row.style.fontWeight = 'bold';
        } else if (index === 1) {
            row.style.backgroundColor = '#c0c0c0'; // Silver
            row.style.fontWeight = 'bold';
        } else if (index === 2) {
            row.style.backgroundColor = '#cd7f32'; // Bronze
            row.style.fontWeight = 'bold';
        }

        // Position
        const posCell = document.createElement('td');
        posCell.textContent = index + 1;
        posCell.style.padding = '8px 10px';
        posCell.style.border = '1px solid #ddd';
        posCell.style.textAlign = 'center';
        posCell.style.fontWeight = 'bold';
        row.appendChild(posCell);

        // Driver
        const driverCell = document.createElement('td');
        const driverName = driverNumberToSurname[driver.driverNum] || `Driver ${driver.driverNum}`;
        driverCell.textContent = `${driverName} (#${driver.driverNum})`;
        driverCell.style.padding = '8px 10px';
        driverCell.style.border = '1px solid #ddd';
        row.appendChild(driverCell);

        // Average position
        const avgCell = document.createElement('td');
        avgCell.textContent = driver.avgPos.toFixed(2);
        avgCell.style.padding = '8px 10px';
        avgCell.style.border = '1px solid #ddd';
        avgCell.style.textAlign = 'center';
        avgCell.style.fontFamily = 'monospace';
        row.appendChild(avgCell);

        tbody.appendChild(row);
    });
}


let globalTyreCurves = null;
let globalBestStrategy = null;
const PIT_STOP_TIME = 20; // Pit loss

// Update custom strategy inputs based on number of stops
function updateCustomStrategyInputs() {
    const numStops = parseInt(document.getElementById('customStopsSelect').value);
    const container = document.getElementById('customStintsContainer');
    container.innerHTML = '';
    
    for (let i = 0; i < numStops; i++) {
        const pitDiv = document.createElement('div');
        pitDiv.style.cssText = 'display: flex; flex-direction: column; gap: 5px;';
        
        const pitLabel = document.createElement('label');
        pitLabel.textContent = `Pit Stop ${i + 1}`;
        pitLabel.style.fontWeight = 'bold';
        pitLabel.style.fontSize = '14px';
        
        const lapInput = document.createElement('input');
        lapInput.type = 'number';
        lapInput.id = `pitLap${i}`;
        lapInput.min = '1';
        lapInput.max = '70';
        lapInput.placeholder = 'Lap #';
        lapInput.style.cssText = 'padding: 8px; border-radius: 5px; border: 1px solid #ccc; width: 80px;';
        
        const tyreSelect = document.createElement('select');
        tyreSelect.id = `pitTyre${i}`;
        tyreSelect.style.cssText = 'padding: 8px; border-radius: 5px; border: 1px solid #ccc;';
        tyreSelect.innerHTML = `
            <option value="SOFT">Soft</option>
            <option value="MEDIUM">Medium</option>
            <option value="HARD">Hard</option>
        `;
        
        const helper = document.createElement('small');
        helper.textContent = 'Change to:';
        helper.style.color = '#666';
        
        pitDiv.appendChild(pitLabel);
        pitDiv.appendChild(lapInput);
        pitDiv.appendChild(helper);
        pitDiv.appendChild(tyreSelect);
        
        container.appendChild(pitDiv);
    }
}

// Calculate lap time for a given compound and lap number within stint
function calculateLapTime(compound, lapInStint, tyreCurves) {
    if (!tyreCurves) return null;
    
    const curve = tyreCurves.find(c => c.compound.toUpperCase() === compound.toUpperCase());
    if (!curve) return null;
    
    return curve.slope * lapInStint + curve.intercept;
}

// Calculate total time for a strategy
function calculateStrategyTime(stints, tyreCurves) {
    let totalTime = 0;
    let lapCount = 0;
    
    for (let stintIdx = 0; stintIdx < stints.length; stintIdx++) {
        const stint = stints[stintIdx];
        const compound = stint.compound;
        const laps = stint.laps;

        for (let lap = 0; lap < laps; lap++) {
            const lapTime = calculateLapTime(compound, lap, tyreCurves);
            if (lapTime === null) return null;
            totalTime += lapTime;
            lapCount++;
        }
        
        // Add pit loss (except for last stint)
        if (stintIdx < stints.length - 1) {
            totalTime += PIT_STOP_TIME;
        }
    }
    
    return { totalTime, lapCount };
}

// Compare custom strategy
async function compareCustomStrategy() {
    const status = document.getElementById('customStratStatus');
    const button = document.getElementById('compareStrategyBtn');
    const resultContainer = document.getElementById('customStrategyResultContainer');
    const resultDiv = document.getElementById('customStrategyResult');
    
    const circuit = document.getElementById('circuitSelect').value;
    const year = document.getElementById('yearSelect').value;
    
    if (!circuit || !year) {
        status.textContent = 'Please select country and year first';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }
    
    // Fetch best strategy to get race length
    if (!globalBestStrategy) {
        status.textContent = 'Please load top strategies first';
        status.className = 'status-message status-error';
        status.style.display = 'block';
        return;
    }
    
    const raceLength = globalBestStrategy.stints.reduce((acc, s) => acc + s.length, 0);
    
    // Collect pit stops and build stints
    const numStops = parseInt(document.getElementById('customStopsSelect').value);
    const startingTyre = document.getElementById('startingTyreSelect').value;
    const pitLaps = [];
    const pitTyres = [];
    
    for (let i = 0; i < numStops; i++) {
        const lap = parseInt(document.getElementById(`pitLap${i}`).value);
        const tyre = document.getElementById(`pitTyre${i}`).value;
        
        if (!lap || lap <= 0 || lap >= raceLength) {
            status.textContent = `Please enter valid pit lap for Pit Stop ${i + 1} (between 1 and ${raceLength - 1})`;
            status.className = 'status-message status-error';
            status.style.display = 'block';
            return;
        }
        
        pitLaps.push(lap);
        pitTyres.push(tyre);
    }
    
    // Check pit laps are in ascending order
    for (let i = 1; i < pitLaps.length; i++) {
        if (pitLaps[i] <= pitLaps[i - 1]) {
            status.textContent = 'Pit laps must be in ascending order';
            status.className = 'status-message status-error';
            status.style.display = 'block';
            return;
        }
    }
    
    // Build stints from pit stops
    const stints = [];
    let currentLap = 0;
    let currentTyre = startingTyre;
    
    for (let i = 0; i < numStops; i++) {
        const pitLap = pitLaps[i];
        const stintLength = pitLap - currentLap;
        stints.push({ compound: currentTyre, laps: stintLength });
        currentLap = pitLap;
        currentTyre = pitTyres[i];
    }
    
    // Final stint to end of race
    const finalStintLength = raceLength - currentLap;
    stints.push({ compound: currentTyre, laps: finalStintLength });
    
    button.disabled = true;
    status.textContent = 'Calculating...';
    status.className = 'status-message status-loading';
    status.style.display = 'block';
    
    try {
        // Fetch tyre curves if not already loaded
        if (!globalTyreCurves) {
            const curvesUrl = `http://localhost:5000/api/solver/tyre-curves?circuit=${circuit}&year=${year}`;
            const curvesResp = await fetch(curvesUrl);
            const curvesData = await curvesResp.json();
            
            if (!curvesData.success || !curvesData.curves) {
                throw new Error('Failed to load tyre curves');
            }
            
            globalTyreCurves = curvesData.curves;
        }
        
        // Fetch best strategy if not already loaded
        if (!globalBestStrategy) {
            const stratsUrl = `http://localhost:5000/api/solver/top-strategies?circuit=${circuit}&year=${year}`;
            const stratsResp = await fetch(stratsUrl);
            const stratsData = await stratsResp.json();
            
            if (!stratsData.success || !stratsData.strategies || stratsData.strategies.length === 0) {
                throw new Error('Failed to load top strategies');
            }
            
            globalBestStrategy = stratsData.strategies[0];
        }
        
        // Calculate custom strategy time
        const customResult = calculateStrategyTime(stints, globalTyreCurves);
        if (!customResult) {
            throw new Error('Failed to calculate custom strategy time');
        }
        
        // Calculate best strategy time
        const bestStints = globalBestStrategy.stints.map(s => ({
            compound: s.compound,
            laps: s.length
        }));
        const bestResult = calculateStrategyTime(bestStints, globalTyreCurves);
        if (!bestResult) {
            throw new Error('Failed to calculate best strategy time');
        }
        
        // Calculate delta
        const delta = customResult.totalTime - bestResult.totalTime;
        
        resultDiv.innerHTML = '';
        
        // Calculate pit laps for best strategy
        const bestPitLaps = [];
        let lapCounter = 0;
        for (let i = 0; i < globalBestStrategy.stints.length - 1; i++) {
            lapCounter += globalBestStrategy.stints[i].length;
            bestPitLaps.push(lapCounter);
        }
        
        // Best strategy display
        const bestRow = createStrategyDisplayRow(
            'Best Strategy',
            bestStints,
            bestResult.totalTime,
            0,
            bestResult.lapCount,
            bestPitLaps
        );
        resultDiv.appendChild(bestRow);
        
        // Custom strategy display
        const customRow = createStrategyDisplayRow(
            'Your Strategy',
            stints,
            customResult.totalTime,
            delta,
            customResult.lapCount,
            pitLaps
        );
        resultDiv.appendChild(customRow);
        
        // Check if strategy uses only one compound
        const uniqueCompounds = new Set(stints.map(s => s.compound.toUpperCase()));
        const isIllegal = uniqueCompounds.size < 2;
        
        let statusText = '';
        if (isIllegal) {
            statusText = delta > 0 
                ? `Your strategy is ${delta.toFixed(3)}s slower (Illegal strategy - must use at least 2 different compounds)` 
                : delta < 0 
                ? `Your strategy is ${Math.abs(delta).toFixed(3)}s faster! (Illegal strategy - must use at least 2 different compounds)` 
                : 'Your strategy matches the best time! (Illegal strategy - must use at least 2 different compounds)';
        } else {
            statusText = delta > 0 
                ? `Your strategy is ${delta.toFixed(3)}s slower` 
                : delta < 0 
                ? `Your strategy is ${Math.abs(delta).toFixed(3)}s faster!` 
                : 'Your strategy matches the best time!';
        }
        
        status.textContent = statusText;
        status.className = (delta > 0 || isIllegal) ? 'status-message status-error' : 'status-message status-success';
        status.style.display = 'block';
        resultContainer.style.display = 'block';
        
    } catch (error) {
        status.textContent = 'Error: ' + error.message;
        status.className = 'status-message status-error';
        status.style.display = 'block';
        console.error('Error comparing strategy:', error);
    } finally {
        button.disabled = false;
    }
}

// Create strategy display row with colored bars
function createStrategyDisplayRow(label, stints, totalTime, delta, totalLaps, pitLaps) {
    const rowDiv = document.createElement('div');
    rowDiv.style.cssText = 'background: #f9f9f9; padding: 15px; border-radius: 8px; border: 2px solid #ddd;';
    
    // Header with label and time info
    const headerDiv = document.createElement('div');
    headerDiv.style.cssText = 'display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;';
    
    const labelSpan = document.createElement('span');
    labelSpan.textContent = label;
    labelSpan.style.cssText = 'font-weight: bold; font-size: 16px;';
    
    const timeDiv = document.createElement('div');
    timeDiv.style.cssText = 'display: flex; gap: 15px; align-items: center;';
    
    const totalTimeSpan = document.createElement('span');
    totalTimeSpan.textContent = `Total: ${formatTime(totalTime)}`;
    totalTimeSpan.style.cssText = 'font-weight: bold;';
    
    timeDiv.appendChild(totalTimeSpan);
    
    if (delta !== 0) {
        const deltaSpan = document.createElement('span');
        deltaSpan.textContent = delta > 0 ? `+${delta.toFixed(3)}s` : `${delta.toFixed(3)}s`;
        deltaSpan.style.cssText = `font-weight: bold; color: ${delta > 0 ? '#d9534f' : '#5cb85c'};`;
        timeDiv.appendChild(deltaSpan);
    }
    
    headerDiv.appendChild(labelSpan);
    headerDiv.appendChild(timeDiv);
    rowDiv.appendChild(headerDiv);
    
    // Strategy bar
    const barWrapper = document.createElement('div');
    barWrapper.className = 'strategy-bar-wrapper';
    barWrapper.style.cssText = 'position: relative; width: 100%; height: 35px; background: #f3f3f3; border-radius: 6px; overflow: hidden; border: 1px solid #ddd;';
    
    // Add lap ticks
    const ticks = document.createElement('div');
    ticks.className = 'lap-ticks';
    const tickInterval = Math.max(1, Math.ceil(totalLaps / 6));
    for (let lap = 1; lap <= totalLaps; lap += tickInterval) {
        const tick = document.createElement('span');
        tick.className = 'lap-tick';
        const leftPct = ((lap - 1) / totalLaps) * 100;
        tick.style.left = leftPct + '%';
        tick.textContent = lap;
        ticks.appendChild(tick);
    }
    barWrapper.appendChild(ticks);
    
    // Add stint segments
    stints.forEach(stint => {
        const seg = document.createElement('div');
        seg.className = 'stint-segment';
        const pct = (stint.laps / totalLaps) * 100;
        seg.style.width = pct + '%';
        seg.style.height = '100%';
        seg.style.display = 'inline-block';
        seg.style.boxSizing = 'border-box';
        
        const compoundUpper = stint.compound.toUpperCase();
        seg.style.backgroundColor = compoundUpper.includes('SOFT') ? '#FF0000'
            : compoundUpper.includes('MEDIUM') ? '#FFFF00' : '#888888';
        seg.title = `${stint.compound} (${stint.laps} laps)`;
        
        barWrapper.appendChild(seg);
    });
    
    // Add black pit stop lines
    if (pitLaps && pitLaps.length > 0) {
        pitLaps.forEach(pitLap => {
            const line = document.createElement('div');
            line.style.cssText = 'position: absolute; top: 0; height: 100%; width: 3px; background-color: #000; z-index: 20;';
            const leftPct = (pitLap / totalLaps) * 100;
            line.style.left = leftPct + '%';
            line.title = `Pit on lap ${pitLap}`;
            barWrapper.appendChild(line);
        });
    }
    
    rowDiv.appendChild(barWrapper);
    
    // Strategy details
    const detailsDiv = document.createElement('div');
    detailsDiv.style.cssText = 'margin-top: 10px; font-size: 13px; color: #666;';
    const strategyText = stints.map(s => `${s.compound} (${s.laps}L)`).join(' → ');
    detailsDiv.textContent = `Strategy: ${strategyText}`;
    rowDiv.appendChild(detailsDiv);
    
    return rowDiv;
}

// Ensure monte dropdown updates when page loads with country/year change
document.addEventListener('DOMContentLoaded', () => {
    const selectYear = document.getElementById('yearSelect');
    const selectCountry = document.getElementById('circuitSelect');
    if (selectYear) selectYear.addEventListener('change', () => { /* no-op until load */ });
    if (selectCountry) selectCountry.addEventListener('change', () => { /* no-op until load */ });
    
    // Initialize custom strategy inputs
    if (typeof updateCustomStrategyInputs === 'function') {
        updateCustomStrategyInputs();
    }
});
