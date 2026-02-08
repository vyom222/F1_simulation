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
    }
}