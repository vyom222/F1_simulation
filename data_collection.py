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
CACHE_DIR = "cache"
os.makedirs(CACHE_DIR, exist_ok=True)

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

# Initial parameters
COUNTRY = "Spain"
YEAR = 2024
SESSION_TYPE = "Practice"
COMPOUNDS = ["SOFT", "MEDIUM", "HARD"] 
SECONDS_SAVED_PER_LAP_FUEL = 0.045

# Get session keys for later API calls
sessions_url = f"https://api.openf1.org/v1/sessions?country_name={COUNTRY}&year={YEAR}&session_type={SESSION_TYPE}"
sessions = fetch_and_cache(sessions_url, f"sessions_{COUNTRY}_{YEAR}_{SESSION_TYPE}.json")
session_keys = [s['session_key'] for s in sessions]
print(session_keys)