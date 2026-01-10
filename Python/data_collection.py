import os
import json
import requests
import certifi
from collections import defaultdict

import numpy as np
import matplotlib.pyplot as plt
from sklearn.linear_model import LinearRegression, HuberRegressor, RANSACRegressor
from scipy.optimize import minimize

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

# Tyre degradation parameters 
DEGRADATION_FACTOR = 0.5
TARGET_SOFT_SLOPE = 0.1 * DEGRADATION_FACTOR  # Target degradation rate for soft tyres (seconds per lap)
TARGET_MEDIUM_SLOPE = 0.075 * DEGRADATION_FACTOR 
TARGET_HARD_SLOPE = 0.05 * DEGRADATION_FACTOR
INTERCEPT_DIFF = 0.3         # Minimum difference in first lap pace between tyre compounds


def fit_tyres_jointly(data_dict):
    """
    Jointly fits all three tyre compounds with physical constraints:
    - Target slopes: Soft ≈ TARGET_SOFT_SLOPE, Medium ≈ TARGET_MEDIUM_SLOPE, Hard ≈ TARGET_HARD_SLOPE
    - Intercept ordering: Hard > Medium > Soft (with minimum INTERCEPT_DIFF between each)
    - Slope ordering: Soft > Medium > Hard (soft degrades fastest)
    
    Parameters:
    data_dict: dict with keys "SOFT", "MEDIUM", "HARD", each containing {"X": array, "y": array}
    
    Returns:
    dict with fitted slopes and intercepts for each compound
    """
    compounds = ["SOFT", "MEDIUM", "HARD"]
    
    # Check we have all three compounds
    if not all(c in data_dict for c in compounds):
        return None
    
    # Get initial estimates using HuberRegressor for each compound
    initial_params = {}
    for compound in compounds:
        X = data_dict[compound]["X"]
        y = data_dict[compound]["y"]
        if len(X) < 10:
            return None
        
        model = HuberRegressor(epsilon=1.35, max_iter=200).fit(X, y)
        initial_params[compound] = {
            "slope": max(0.001, model.coef_[0]),  # Ensure positive
            "intercept": model.intercept_
        }
    
    # Prepare initial parameter vector
    x0 = np.array([
        initial_params["SOFT"]["slope"],
        initial_params["MEDIUM"]["slope"],
        initial_params["HARD"]["slope"],
        initial_params["SOFT"]["intercept"],
        initial_params["MEDIUM"]["intercept"],
        initial_params["HARD"]["intercept"]
    ])
    
    # Objective function: minimize sum of squared residuals for all compounds
    # Plus penalty for deviating from target slopes
    def objective(x):
        total_error = 0.0
        
        # Fit error for each compound
        for i, compound in enumerate(compounds):
            X = data_dict[compound]["X"]
            y = data_dict[compound]["y"]
            slope = x[i]
            intercept = x[i + 3]
            predicted = slope * X.flatten() + intercept
            residuals = y - predicted
            total_error += np.sum(residuals ** 2)
        
        # Penalty for deviating from target slopes (weighted by data size)
        target_slopes = [TARGET_SOFT_SLOPE, TARGET_MEDIUM_SLOPE, TARGET_HARD_SLOPE]
        slope_penalty_weight = 100.0  # Weight for slope target penalty
        for i, target in enumerate(target_slopes):
            slope_diff = (x[i] - target) ** 2
            # Weight by inverse of data size (more data = less penalty for deviation)
            data_size = len(data_dict[compounds[i]]["X"])
            weight = slope_penalty_weight / max(1, data_size / 50)
            total_error += weight * slope_diff
        
        return total_error
    
    # Constraints
    margin = 0.001
    constraints = [
        # Slope constraints: SOFT > MEDIUM > HARD
        {'type': 'ineq', 'fun': lambda x: x[0] - x[1] - margin},  # SOFT slope > MEDIUM slope
        {'type': 'ineq', 'fun': lambda x: x[1] - x[2] - margin},  # MEDIUM slope > HARD slope
        # Intercept constraints: HARD > MEDIUM > SOFT (with minimum difference)
        {'type': 'ineq', 'fun': lambda x: x[5] - x[4] - INTERCEPT_DIFF},  # HARD intercept >= MEDIUM intercept + INTERCEPT_DIFF
        {'type': 'ineq', 'fun': lambda x: x[4] - x[3] - INTERCEPT_DIFF},  # MEDIUM intercept >= SOFT intercept + INTERCEPT_DIFF
    ]
    
    # Bounds: reasonable ranges for slopes and intercepts
    bounds = [
        (0.001, 0.5),   # soft_slope (positive, reasonable degradation)
        (0.001, 0.5),   # med_slope
        (0.001, 0.5),   # hard_slope
        (50, 200),      # soft_int (lap times in seconds)
        (50, 200),      # med_int
        (50, 200),      # hard_int
    ]
    
    try:
        result = minimize(objective, x0, method='SLSQP', bounds=bounds, constraints=constraints, options={'maxiter': 1000})
        if result.success:
            return {
                "SOFT": {"Slope": result.x[0], "Intercept": result.x[3]},
                "MEDIUM": {"Slope": result.x[1], "Intercept": result.x[4]},
                "HARD": {"Slope": result.x[2], "Intercept": result.x[5]}
            }
        else:
            # If optimization fails, return initial estimates with constraints applied
            # print(f"Warning: Joint optimization did not converge, using initial estimates with constraints")
            return {
                "SOFT": {"Slope": initial_params["SOFT"]["slope"], "Intercept": initial_params["SOFT"]["intercept"]},
                "MEDIUM": {"Slope": initial_params["MEDIUM"]["slope"], "Intercept": initial_params["MEDIUM"]["intercept"]},
                "HARD": {"Slope": initial_params["HARD"]["slope"], "Intercept": initial_params["HARD"]["intercept"]}
            }
    except Exception as e:
        # print(f"Error in joint optimization: {e}, using initial estimates")
        return {
            "SOFT": {"Slope": initial_params["SOFT"]["slope"], "Intercept": initial_params["SOFT"]["intercept"]},
            "MEDIUM": {"Slope": initial_params["MEDIUM"]["slope"], "Intercept": initial_params["MEDIUM"]["intercept"]},
            "HARD": {"Slope": initial_params["HARD"]["slope"], "Intercept": initial_params["HARD"]["intercept"]}
        }


def get_curves(country, year):
    results = []
    results_dict = {}  # Store results by compound for constraint enforcement

    sessions_url = (
        f"https://api.openf1.org/v1/sessions?"
        f"country_name={country}&year={year}&session_type={SESSION_TYPE}"
    )
    sessions = fetch_and_cache(
        sessions_url,
        f"sessions_{country}_{year}_{SESSION_TYPE}.json"
    )
    session_keys = [s["session_key"] for s in sessions]

    for compound in COMPOUNDS:
        all_X = []
        all_y = []

        for session in session_keys:
            stints = fetch_and_cache(
                f"https://api.openf1.org/v1/stints?session_key={session}",
                f"stints_{session}.json"
            )
            laps = fetch_and_cache(
                f"https://api.openf1.org/v1/laps?session_key={session}",
                f"laps_{session}.json"
            )

            stints = [s for s in stints if is_valid_stint(s)]
            stints = [s for s in stints if (s["lap_end"] - s["lap_start"]) > 5]

            laps_by_driver = defaultdict(dict)
            for lap in laps:
                d = lap.get("driver_number")
                n = lap.get("lap_number")
                if d is not None and n is not None:
                    laps_by_driver[d][n] = lap

            for stint in stints:
                if stint.get("compound", "").upper() != compound:
                    continue

                driver = stint.get("driver_number")
                start = int(stint["lap_start"])
                end = int(stint["lap_end"])
                tyre_age_start = int(stint.get("tyre_age_at_start", 0))
                stint_length = end - start + 1

                driver_laps = laps_by_driver.get(driver, {})

                for lap_num in range(start + 1, end):
                    lap = driver_laps.get(lap_num)
                    if not lap or lap.get("is_pit_out_lap"):
                        continue
                    try:
                        lap_time = (
                            float(lap["duration_sector_1"])
                            + float(lap["duration_sector_2"])
                            + float(lap["duration_sector_3"])
                        )
                    except:
                        continue

                    tyre_age = tyre_age_start + (lap_num - start)

                    # Fuel correction (restored)
                    laps_of_fuel = stint_length + 2
                    laps_completed = lap_num - start
                    remaining_fuel_laps = max(0, laps_of_fuel - laps_completed)
                    fuel_correction = remaining_fuel_laps * SECONDS_SAVED_PER_LAP_FUEL

                    corrected_time = lap_time - fuel_correction

                    all_X.append(tyre_age)
                    all_y.append(corrected_time)

        # if len(all_X) < 10:
        #     print(f"Not enough data for {compound}")
        #     continue

        X = np.array(all_X).reshape(-1, 1)
        y = np.array(all_y)

        # ===== BALANCED ITERATIVE OUTLIER REMOVAL =====
        max_iterations = 5  # Fewer iterations
        min_samples = max(20, int(len(X) * 0.3))  # Keep at least 30% of data
        prev_size = len(X)
        
        for iteration in range(max_iterations):
            if len(X) < min_samples:
                break
                
            # Use HuberRegressor for robust initial fit (less aggressive than RANSAC)
            initial_model = HuberRegressor(epsilon=1.35, max_iter=200).fit(X, y)
            residuals = y - initial_model.predict(X)
            
            # Method 1: Modified Z-score with MAD (balanced threshold)
            median_residual = np.median(residuals)
            mad = np.median(np.abs(residuals - median_residual))
            z_score_votes = np.zeros(len(X), dtype=int)
            if mad > 0:
                modified_z_scores = 0.6745 * (residuals - median_residual) / mad
                z_score_votes = (np.abs(modified_z_scores) < 3.0).astype(int)  # Balanced: 3.0
            
            # Method 2: IQR method (balanced)
            q1 = np.percentile(residuals, 25)
            q3 = np.percentile(residuals, 75)
            iqr = q3 - q1
            iqr_votes = np.zeros(len(X), dtype=int)
            if iqr > 0:
                iqr_lower = q1 - 2.0 * iqr  # Balanced: 2.0
                iqr_upper = q3 + 2.0 * iqr
                iqr_votes = ((residuals >= iqr_lower) & (residuals <= iqr_upper)).astype(int)
            else:
                iqr_votes = np.ones(len(X), dtype=int)
            
            # Method 3: Remove extreme outliers only (beyond 3.5 standard deviations)
            extreme_votes = np.ones(len(X), dtype=int)
            if len(residuals) > 0:
                std_residual = np.std(residuals)
                mean_residual = np.mean(residuals)
                if std_residual > 0:
                    extreme_votes = (np.abs(residuals - mean_residual) < 3.5 * std_residual).astype(int)
            
            # Method 4: RANSAC inlier detection (as additional vote)
            ransac_votes = np.ones(len(X), dtype=int)
            try:
                if len(X) > 10:  # Only use RANSAC if we have enough points
                    ransac = RANSACRegressor(
                        estimator=LinearRegression(),
                        residual_threshold=None,
                        max_trials=100,
                        random_state=42,
                        min_samples=max(3, len(X) // 5)
                    )
                    ransac.fit(X, y)
                    ransac_votes = ransac.inlier_mask_.astype(int)
            except:
                pass
            
            # Majority vote: keep points that pass at least 3 out of 4 methods
            total_votes = z_score_votes + iqr_votes + extreme_votes + ransac_votes
            keep = total_votes >= 3
            
            # Additional check: ensure we don't remove too much
            current_size = np.sum(keep)
            removal_ratio = 1.0 - (current_size / len(X))
            
            # If removing more than 40% in one iteration, be more lenient
            if removal_ratio > 0.4:
                keep = total_votes >= 2  # Lower threshold: at least 2 out of 4 methods
                current_size = np.sum(keep)
            
            # Check if we made progress
            if np.all(keep) or current_size == prev_size:
                break
            
            prev_size = current_size
            X = X[keep]
            y = y[keep]
            
            if len(X) < min_samples:
                break
        
        # if len(X) < 10:
        #     print(f"Not enough data after outlier removal for {compound}")
        #     continue
        
        # Final check: ensure we have enough data and positive slope
        # Use HuberRegressor for final fit
        model = HuberRegressor(epsilon=1.35, max_iter=200).fit(X, y)
        slope = model.coef_[0]
        
        # If slope is negative or very small, we may have removed too many points
        # Try a more lenient pass if slope is problematic
        if slope < 0.001 and len(X) < len(all_X) * 0.5:
            # Re-run with more lenient outlier removal
            X = np.array(all_X).reshape(-1, 1)
            y = np.array(all_y)
            
            initial_model = HuberRegressor(epsilon=1.35, max_iter=200).fit(X, y)
            residuals = y - initial_model.predict(X)
            
            median_residual = np.median(residuals)
            mad = np.median(np.abs(residuals - median_residual))
            if mad > 0:
                modified_z_scores = 0.6745 * (residuals - median_residual) / mad
                keep = np.abs(modified_z_scores) < 3.5  # More lenient
            else:
                keep = np.ones(len(X), dtype=bool)
            
            X = X[keep]
            y = y[keep]
            
            if len(X) >= 10:
                model = HuberRegressor(epsilon=1.35, max_iter=200).fit(X, y)
                slope = model.coef_[0]
        
        intercept = model.intercept_
        
        # # Warn if slope is negative (will be fixed by joint optimization)
        # if slope < 0:
        #     print(f"Warning: Negative slope detected for {compound} ({slope:.6f}). Will be corrected by joint optimization.")


        # Store cleaned data for joint optimization
        results_dict[compound] = {
            "X": X,  # Store cleaned data for plotting and joint fitting
            "y": y,
            "Slope": slope,  # Initial estimate
            "Intercept": intercept  # Initial estimate
        }

    # Joint optimization with physical constraints
    if len(results_dict) == 3:  # Only do joint optimization if we have all three compounds
        # Prepare data dict for joint fitting
        data_dict = {compound: {"X": results_dict[compound]["X"], "y": results_dict[compound]["y"]} 
                     for compound in COMPOUNDS}
        
        # Perform joint optimization
        joint_results = fit_tyres_jointly(data_dict)
        
        if joint_results:
            # Update results with joint fit parameters
            for compound in COMPOUNDS:
                results_dict[compound]["Slope"] = joint_results[compound]["Slope"]
                results_dict[compound]["Intercept"] = joint_results[compound]["Intercept"]
        # else:
        #     print("Warning: Joint optimization failed, using individual fits")
    
    # Generate results for all compounds
    for compound in COMPOUNDS:
        if compound not in results_dict:
            continue
            
        data = results_dict[compound]
        slope = data["Slope"]
        intercept = data["Intercept"]
        
        results.append({
            "Compound": compound,
            "Slope": slope * DEGRADATION_FACTOR,
            "Intercept": intercept
        })

    return results


# if __name__ == "__main__":
#     curves = get_curves("Spain", 2024)
#     print(curves)
