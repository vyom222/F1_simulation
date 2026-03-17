using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Monte_carlo_simulator;
using MySql.Data.MySqlClient;

namespace F1_simulation.Database
{
    public class F1_cache
    {
        private readonly string _connection;

        public F1_cache(string connection)
        {
            _connection = connection;
        }

        public int GetLaps(string circuit)
        {
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                // cross table parameterised SQL
                int laps = 0;
                conn.Open();
                string query = @"
                SELECT Laps
                FROM countries
                WHERE circuitShortName = @circuitName";
                using(MySqlCommand cmd = new MySqlCommand(query,conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            laps = Convert.ToInt32(reader["Laps"]);
                        }
                    }

                }
                return laps;
            }
        }
        // Query the tyre curves
        public List<string> GetTyreCurves(string circuit, int year)
        {
            List<string> curves = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT compound, gradient, intercept
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN TYRECURVES tc
                ON  tc.raceID = r.raceID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            curves.Add($"{reader["compound"]} {reader["gradient"]} {reader["intercept"]}");
                        }
                    }
                }

            }
            return curves;
        }
        // Cache the tyre curves
        public void AddTyreCurves(string circuit, int year, string compound, double gradient, double intercept)
        {
            int raceid = 0;
            // First find the raceID that will be associated with these tyre curves
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                // Only insert if we found a valid raceID
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping tyre curve cache.");
                    return;
                }
                
                // Use INSERT IGNORE to prevent duplicates
                string insertQuery = @"
                INSERT IGNORE INTO TYRECURVES (raceID, compound, gradient, intercept)
                VALUES (@raceID, @compound, @gradient, @intercept)";
                using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@raceID", raceid);
                    cmd.Parameters.AddWithValue("@compound", compound);
                    cmd.Parameters.AddWithValue("@gradient", gradient);
                    cmd.Parameters.AddWithValue("@intercept", intercept);
                    cmd.ExecuteNonQuery();
                }
            } 
        }

        // query sessions
        public List<int> GetSessionKeys(string circuit, int year)
        {
            List<int> keys = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT sessionkey
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN Sessions s
                ON  s.raceID = r.raceID
                WHERE CircuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            keys.Add(Convert.ToInt32(reader["sessionkey"]));
                        }
                    }
                }
            }
            return keys;
        }

        public void AddSessions(string circuit, int year, List<int> session_keys)
        {
            int raceid = 0;
            // Find the raceID
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                // Only insert if we found a valid raceID
                // Good error handling - user can't see this but I can
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping session cache.");
                    return;
                }
                
                string insertQuery = @"
                INSERT IGNORE INTO Sessions (sessionKey, RaceID)
                VALUES (@sessionkey, @RaceID)";
                foreach(int key in session_keys)
                {
                    using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@sessionkey", key);
                        cmd.Parameters.AddWithValue("@raceID", raceid);
                        cmd.ExecuteNonQuery();
                    
                    }
                }
            } 
        }

        // Qualifying cache methods
        public List<Dictionary<string, object>> GetQualifying(string circuit, int year)
        {
            List<Dictionary<string, object>> qualifying = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT q.position, q.delta, d.driverNumber
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN Qualifying q
                ON q.raceID = r.raceID
                JOIN DRIVERS d
                ON d.driverID = q.driverID
                WHERE circuitShortName = @circuitName AND year = @year
                ORDER BY q.position
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            var entry = new Dictionary<string, object>
                            {
                                ["position"] = Convert.ToInt32(reader["position"]),
                                ["driver_number"] = Convert.ToInt32(reader["driverNumber"]),
                                ["gap"] = reader["delta"].ToString()!
                            };
                            qualifying.Add(entry);
                        }
                    }
                }
            }
            return qualifying;
        }

        public void AddQualifying(string circuit, int year, List<Dictionary<string, object>> qualifyingData)
        {
            int raceid = 0;
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping qualifying cache.");
                    return;
                }

                foreach (var entry in qualifyingData)
                {
                    int driverId = GetDriver(conn, Convert.ToInt32(entry["driver_number"]));
                    
                    string insertQuery = @"
                    INSERT IGNORE INTO Qualifying (raceID, driverID, position, delta)
                    VALUES (@raceID, @driverID, @position, @delta)";
                    
                    using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@raceID", raceid);
                        cmd.Parameters.AddWithValue("@driverID", driverId);
                        cmd.Parameters.AddWithValue("@position", entry["position"]);
                        cmd.Parameters.AddWithValue("@delta", entry["gap"].ToString());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // Race pace cache methods
        public List<Dictionary<string, object>> GetRacePace(string circuit, int year)
        {
            List<Dictionary<string, object>> racePace = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT rp.position, rp.delta, d.driverNumber
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN RacePace rp
                ON rp.raceID = r.raceID
                JOIN DRIVERS d
                ON d.driverID = rp.driverID
                WHERE circuitShortName = @circuitName AND year = @year
                ORDER BY rp.position
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            var entry = new Dictionary<string, object>
                            {
                                ["position"] = Convert.ToInt32(reader["position"]),
                                ["driver_number"] = Convert.ToInt32(reader["driverNumber"]),
                                ["gap_to_fastest"] = reader["delta"].ToString()!
                            };
                            racePace.Add(entry);
                        }
                    }
                }
            }
            return racePace;
        }

        public void AddRacePace(string circuit, int year, List<Dictionary<string, object>> racePaceData)
        {
            int raceid = 0;
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping race pace cache.");
                    return;
                }

                foreach (var entry in racePaceData)
                {
                    // Get driverID
                    int driverId = GetDriver(conn, Convert.ToInt32(entry["driver_number"]));
                    
                    string insertQuery = @"
                    INSERT IGNORE INTO RacePace (raceID, driverID, position, delta)
                    VALUES (@raceID, @driverID, @position, @delta)";
                    
                    using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@raceID", raceid);
                        cmd.Parameters.AddWithValue("@driverID", driverId);
                        cmd.Parameters.AddWithValue("@position", entry["position"]);
                        cmd.Parameters.AddWithValue("@delta", entry["gap_to_fastest"].ToString());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // Race simulation cache methods
        public List<Dictionary<string, object>> GetRaceSimulation(string circuit, int year)
        {
            List<Dictionary<string, object>> raceSimulation = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT rs.Position, rs.Strategy, rs.TotalTime, d.driverNumber
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN RaceSimulation rs
                ON rs.raceID = r.raceID
                JOIN DRIVERS d
                ON d.driverID = rs.driverID
                WHERE circuitShortName = @circuitName AND year = @year
                ORDER BY rs.Position
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            var entry = new Dictionary<string, object>
                            {
                                ["position"] = Convert.ToInt32(reader["Position"]),
                                ["driverNumber"] = Convert.ToInt32(reader["driverNumber"]),
                                ["strategy"] = reader["Strategy"].ToString()!,
                                ["totalTime"] = Convert.ToDouble(reader["TotalTime"])
                            };
                            raceSimulation.Add(entry);
                        }
                    }
                }
            }
            return raceSimulation;
        }

        public void AddRaceSimulation(string circuit, int year, List<Dictionary<string, object>> raceSimData)
        {
            int raceid = 0;
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping race simulation cache.");
                    return;
                }

                foreach (var entry in raceSimData)
                {
                    // Get or create driver
                    int driverId = GetDriver(conn, Convert.ToInt32(entry["driverNumber"]));
                    
                    string insertQuery = @"
                    INSERT IGNORE INTO RaceSimulation (raceID, driverID, Position, Strategy, TotalTime)
                    VALUES (@raceID, @driverID, @position, @strategy, @totalTime)";
                    
                    using (MySqlCommand cmd = new MySqlCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@raceID", raceid);
                        cmd.Parameters.AddWithValue("@driverID", driverId);
                        cmd.Parameters.AddWithValue("@position", entry["position"]);
                        cmd.Parameters.AddWithValue("@strategy", entry["strategy"].ToString());
                        cmd.Parameters.AddWithValue("@totalTime", entry["totalTime"]);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // Top strategies cache methods
        public List<Dictionary<string, object>> GetTopStrategies(string circuit, int year)
        {
            List<Dictionary<string, object>> strategies = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT ts.StrategyID, ts.StrategyName, ts.ExpectedTotalTime, ts.`Rank`
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                JOIN TopStrategies ts
                ON ts.raceID = r.raceID
                WHERE circuitShortName = @circuitName AND year = @year
                ORDER BY ts.`Rank`
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            int strategyId = Convert.ToInt32(reader["StrategyID"]);
                            var entry = new Dictionary<string, object>
                            {
                                ["strategy_id"] = strategyId,
                                ["strategy_name"] = reader["StrategyName"].ToString()!,
                                ["best_time"] = Convert.ToDouble(reader["ExpectedTotalTime"]),
                                ["rank"] = Convert.ToInt32(reader["Rank"]),
                                ["stints"] = new List<Dictionary<string, object>>(),
                                ["windows"] = new List<Dictionary<string, object>>()
                            };
                            strategies.Add(entry);
                        }
                    }
                }

                // Now fetch stints and windows for each strategy
                foreach (var strategy in strategies)
                {
                    int strategyId = (int)strategy["strategy_id"];
                    
                    // Get stints
                    string stintQuery = @"
                    SELECT StintNumber, Compound, Start, End
                    FROM StrategyStints
                    WHERE StrategyID = @strategyId
                    ORDER BY StintNumber";
                    
                    var stints = new List<Dictionary<string, object>>();
                    using (MySqlCommand cmd = new MySqlCommand(stintQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@strategyId", strategyId);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while(reader.Read())
                            {
                                stints.Add(new Dictionary<string, object>
                                {
                                    ["stint_number"] = Convert.ToInt32(reader["StintNumber"]),
                                    ["compound"] = reader["Compound"].ToString()!,
                                    ["start"] = Convert.ToInt32(reader["Start"]),
                                    ["end"] = Convert.ToInt32(reader["End"])
                                });
                            }
                        }
                    }
                    strategy["stints"] = stints;

                    // Get windows
                    string windowQuery = @"
                    SELECT WindowStart, WindowEnd
                    FROM StrategyWindows
                    WHERE StrategyID = @strategyId
                    ORDER BY WindowStart";
                    
                    var windows = new List<Dictionary<string, object>>();
                    using (MySqlCommand cmd = new MySqlCommand(windowQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@strategyId", strategyId);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while(reader.Read())
                            {
                                windows.Add(new Dictionary<string, object>
                                {
                                    ["min"] = Convert.ToInt32(reader["WindowStart"]),
                                    ["max"] = Convert.ToInt32(reader["WindowEnd"])
                                });
                            }
                        }
                    }
                    strategy["windows"] = windows;
                }
            }
            return strategies;
        }

        public void AddTopStrategies(string circuit, int year, List<Dictionary<string, object>> strategiesData)
        {
            int raceid = 0;
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT raceID
                FROM COUNTRIES c 
                JOIN RACES r
                ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year
                ";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            raceid = Convert.ToInt32(reader["raceID"]);
                        }
                    }
                }
                
                if (raceid == 0)
                {
                    Console.WriteLine($"Warning: No race found for circuit '{circuit}' and year {year}. Skipping top strategies cache.");
                    return;
                }

                int rank = 1;
                foreach (var strategyData in strategiesData)
                {
                    // Insert the strategy
                    string insertStrategyQuery = @"
                    INSERT IGNORE INTO TopStrategies (raceID, StrategyName, ExpectedTotalTime, `Rank`)
                    VALUES (@raceID, @strategyName, @expectedTotalTime, @rank);
                    SELECT LAST_INSERT_ID();"; // Get the StrategyID that I just inserted
                    
                    int strategyId;
                    using (MySqlCommand cmd = new MySqlCommand(insertStrategyQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@raceID", raceid);
                        cmd.Parameters.AddWithValue("@strategyName", strategyData["strategy_name"].ToString());
                        cmd.Parameters.AddWithValue("@expectedTotalTime", strategyData["best_time"]);
                        cmd.Parameters.AddWithValue("@rank", rank);
                        
                        var result = cmd.ExecuteScalar();
                        strategyId = Convert.ToInt32(result);
                    }

                    // Insert stints
                    if (strategyData.ContainsKey("stints") && strategyData["stints"] is List<Dictionary<string, object>> stints)
                    {
                        foreach (var stint in stints)
                        {
                            string insertStintQuery = @"
                            INSERT IGNORE INTO StrategyStints (StrategyID, StintNumber, Compound, Start, End)
                            VALUES (@strategyID, @stintNumber, @compound, @start, @end)";
                            
                            using (MySqlCommand cmd = new MySqlCommand(insertStintQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@strategyID", strategyId);
                                cmd.Parameters.AddWithValue("@stintNumber", stint["stint_number"]);
                                cmd.Parameters.AddWithValue("@compound", stint["compound"].ToString());
                                cmd.Parameters.AddWithValue("@start", stint["start"]);
                                cmd.Parameters.AddWithValue("@end", stint["end"]);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    // Insert windows
                    if (strategyData.ContainsKey("windows") && strategyData["windows"] is List<Dictionary<string, object>> windows)
                    {
                        foreach (var window in windows)
                        {
                            string insertWindowQuery = @"
                            INSERT IGNORE INTO StrategyWindows (StrategyID, WindowStart, WindowEnd)
                            VALUES (@strategyID, @windowStart, @windowEnd)";
                            
                            using (MySqlCommand cmd = new MySqlCommand(insertWindowQuery, conn))
                            {
                                cmd.Parameters.AddWithValue("@strategyID", strategyId);
                                cmd.Parameters.AddWithValue("@windowStart", window["min"]);
                                cmd.Parameters.AddWithValue("@windowEnd", window["max"]);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    rank++;
                }
            }
        }

        // Helper method to get or create a driver by driver number
        private int GetDriver(MySqlConnection conn, int driverNumber)
        {
            string query = "SELECT driverID FROM DRIVERS WHERE driverNumber = @driverNumber";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@driverNumber", driverNumber);
                var result = cmd.ExecuteScalar();

                return Convert.ToInt32(result);
            }
        }

        // Get all drivers with their numbers and names
        public List<Dictionary<string, object>> GetAllDrivers()
        {
            List<Dictionary<string, object>> drivers = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT driverNumber, 
                driverName FROM DRIVERS";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            drivers.Add(new Dictionary<string, object>
                            {
                                ["driver_number"] = Convert.ToInt32(reader["driverNumber"]),
                                ["driver_name"] = reader["driverName"].ToString()!
                            });
                        }
                    }
                }
            }
            return drivers;
        }

        // Get all teams with their colors
        public List<Dictionary<string, object>> GetAllTeams()
        {
            List<Dictionary<string, object>> teams = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT teamName, colour 
                FROM TEAMS";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            teams.Add(new Dictionary<string, object>
                            {
                                ["team_name"] = reader["teamName"].ToString()!,
                                ["colour"] = reader["colour"].ToString()!
                            });
                        }
                    }
                }
            }
            return teams;
        }

        // Get driver-team mappings by year
        public List<Dictionary<string, object>> GetDriverTeamsByYear(int year)
        {
            List<Dictionary<string, object>> driverTeams = [];
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                conn.Open();
                string query = @"
                SELECT d.driverNumber, t.teamName, t.colour
                FROM DRIVERTEAMS dt
                JOIN DRIVERS d
                ON d.driverID = dt.driverID
                JOIN TEAMS t
                ON t.teamID = dt.teamID
                WHERE dt.year = @year";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@year", year);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            driverTeams.Add(new Dictionary<string, object>
                            {
                                ["driver_number"] = Convert.ToInt32(reader["driverNumber"]),
                                ["team_name"] = reader["teamName"].ToString()!,
                                ["colour"] = reader["colour"].ToString()!
                            });
                        }
                    }
                }
            }
            return driverTeams;
        }

        // Check if a race exists for a given circuit and year
        public bool RaceExists(string circuit, int year)
        {
            using (MySqlConnection conn = new MySqlConnection(_connection))
            {
                // Aggregate SQL queries
                conn.Open();
                string query = @"
                SELECT COUNT(*) as count
                FROM COUNTRIES c 
                JOIN RACES r ON r.countryID = c.countryID
                WHERE circuitShortName = @circuitName AND year = @year";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@circuitName", circuit);
                    cmd.Parameters.AddWithValue("@year", year);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return Convert.ToInt32(reader["count"]) > 0;
                        }
                    }
                }
            }
            return false;
        }
    }
}