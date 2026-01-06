import os
import json
import requests
import certifi
from collections import defaultdict
import sys

import numpy as np
import matplotlib.pyplot as plt
from sklearn.linear_model import LinearRegression, RANSACRegressor, HuberRegressor
from scipy.optimize import curve_fit

### CHANGE LATER TO DATABASE INSTEAD 

# Changed due to all the refactoring with the directories
BASE_DIR = os.path.dirname(os.path.abspath(__file__))   # python/
PROJECT_ROOT = os.path.abspath(os.path.join(BASE_DIR, ".."))

ASSETS_DIR = os.path.join(PROJECT_ROOT, "Assets")
CACHE_DIR = os.path.join(ASSETS_DIR, "cache")
PLOT_DIR = os.path.join(ASSETS_DIR, "Tyre_degradation")

os.makedirs(CACHE_DIR, exist_ok=True)
os.makedirs(PLOT_DIR, exist_ok=True)


def fetch_and_cache(url, fname):
    path = os.path.join(CACHE_DIR, fname)

    if os.path.exists(path):
        with open(path, "r") as f:
            data = json.load(f)
    else:
        response = requests.get(url, verify=certifi.where(), timeout=30)
        response.raise_for_status()
        data = response.json()

        with open(path, "w") as f:
            json.dump(data, f)

    return data

### END OF SECTION THAT NEEDS UPDATING LATER

# Check that data is not missing entries
def is_valid_stint(stint):
    return (
        stint.get("lap_start") is not None
        and stint.get("lap_end") is not None
        and isinstance(stint.get("lap_start"), int)
        and isinstance(stint.get("lap_end"), int)
        and stint["lap_end"] >= stint["lap_start"]
    )

# Initial parameters
SESSION_TYPE = "Practice"
COMPOUNDS = ["SOFT", "MEDIUM", "HARD"] 
SECONDS_SAVED_PER_LAP_FUEL = 0.045


def get_curves(country, year):
    results = []
    # Get session keys for later API calls
    sessions_url = f"https://api.openf1.org/v1/sessions?country_name={country}&year={year}&session_type={SESSION_TYPE}"
    sessions = fetch_and_cache(sessions_url, f"sessions_{country}_{year}_{SESSION_TYPE}.json")
    session_keys = [s['session_key'] for s in sessions]

    for compound in COMPOUNDS:
        rows = []
        for session in session_keys:
            stints = fetch_and_cache(f"https://api.openf1.org/v1/stints?session_key={session}", f"stints_{session}.json")
            laps = fetch_and_cache(f"https://api.openf1.org/v1/laps?session_key={session}", f"laps_{session}.json")
            
            # Remove invalid stints (None, missing, broken)
            stints = [s for s in stints if is_valid_stint(s)]
            # Long run stints only
            stints = [s for s in stints if (s["lap_end"] - s["lap_start"]) > 5]


            # Group laps by driver, useful for team analysis later
            laps_by_driver = defaultdict(dict)
            for lap in laps:
                driver_num = lap.get("driver_number") # Caution for missing data
                lap_num = lap.get("lap_number")
                if driver_num is not None and lap_num is not None:
                    laps_by_driver[driver_num][lap_num] = lap

            # Iterate through each stint
            for stint in stints:
                tyre = stint.get("compound")
                if tyre.upper() != compound.upper():
                    continue
                
                # Get stint length and tyre age
                driver_num = stint.get("driver_number")
                try:
                    start = int(stint.get("lap_start"))
                    end = int(stint.get("lap_end"))
                except (TypeError, ValueError):
                    continue
                stint_length = end - start + 1 # Discard final lap of data? Check later
                tyre_age_start = int(stint.get("tyre_age_at_start", 0))

                driver_laps = laps_by_driver.get(driver_num, {})
                for lap_num in range(start, end):
                    lap = driver_laps.get(lap_num)
                    if not lap or lap.get("is_pit_out_lap"):
                        continue
                    try:
                        lap_time = (float(lap["duration_sector_1"])
                                    + float(lap["duration_sector_2"])
                                    + float(lap["duration_sector_3"]))
                    except:
                        continue
                    
                    tyre_age = tyre_age_start + (lap_num - start)
                    rows.append({
                        "lap_time": lap_time,
                        "tyre_age": tyre_age,
                        "driver": driver_num,
                        "session": session,
                        "lap_number": lap_num,
                        "stint_start": start,
                        "stint_end": end,
                        "stint_length": stint_length,
                        "tyre_age_at_start": tyre_age_start,
                        "stint_number": stint.get("stint_number")
                    })

                # Filter laps to remove push laps and anomalies
                filtered_rows = []
                groups = defaultdict(list)
                for row in rows:
                    key = (row["driver"], row["session"], row["stint_number"])
                    groups[key].append(row)

                for group in groups.values():
                    lap_times = np.array([r["lap_time"] for r in group])
                    median_time = np.median(lap_times) # Median not skewed
                    for row in group:
                        if row["lap_number"] == row["stint_start"]:
                            continue # Skip first lap
                        if row["lap_time"] > median_time * 1.1:
                            continue
                        if row["lap_time"] > 120:
                            continue
                        else:
                            filtered_rows.append(row)

                rows = filtered_rows

            # Fuel correction
            fuel_corrected_rows = []
            for row in rows:
                laps_of_fuel = row["stint_length"] + 2
                laps_completed = row["lap_number"] - row["stint_start"]
                remaining_fuel_laps = max(0, laps_of_fuel - laps_completed)

                fuel_correction = remaining_fuel_laps * SECONDS_SAVED_PER_LAP_FUEL
                corrected_lap_time = row["lap_time"] - fuel_correction
                fuel_corrected_rows.append({**row, "fuel_corrected_lap_time": corrected_lap_time})

            

            # Store data for plotting
            all_fuel_corrected_rows = []

            # Linear regression on tyre age vs fuel corrected lap time
            if not fuel_corrected_rows:
                print(f"No fuel corrected rows for {compound} tyres in {session}")
                continue
            all_fuel_corrected_rows.extend(fuel_corrected_rows)

        # After processing all sessions for the compound

        # print(len(all_fuel_corrected_rows))
        X = np.array([r["tyre_age"] for r in all_fuel_corrected_rows]).reshape(-1, 1)
        y = np.array([r["fuel_corrected_lap_time"] for r in all_fuel_corrected_rows])
        if len(X) < 2:
            print(f"Not enough data for {compound} tyres")
            continue
        model = LinearRegression()
        model.fit(X, y)
        slope = model.coef_[0]
        intercept = model.intercept_
        # print(f"Compound: {compound}, Tyre Degradation Rate: {slope:.4f} seconds/lap")
        results.append({
            "Compound":compound,
            "Slope":slope,
            "Intercept":intercept })
    return results
            # # Plotting
            # plt.figure(figsize=(10, 6))
            # plt.scatter(X, y, color='blue', label='Data Points')
            # plt.plot(X, model.predict(X), color='red', linewidth=2, label='Linear Regression Fit')
            # plt.title(f'Tyre Degradation for {compound} Tyres')
            # plt.xlabel('Tyre Age (laps)')
            # plt.ylabel('Fuel Corrected Lap Time (seconds)')
            # plt.legend()
            # plt.grid(True)
            # plt.savefig(os.path.join(PLOT_DIR, f"tyre_degradation_{compound}.png"))
            # plt.close()


        

