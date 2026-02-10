using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Monte_carlo_simulator;
using Microsoft.AspNetCore.Mvc;
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

        public void AddTyreCurves(string circuit, int year, string compound, double gradient, double intercept)
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
        // add sessions

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
                    // Get or create driver
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
                    // Get or create driver
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
    }
}