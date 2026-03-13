namespace F1_simulation.Core.Tyres
{
    public abstract class Tyre
    {
        // Protected so inherited classes can access
        protected double Slope { get; }
        protected double Intercept { get; }
        public string Name { get; }
        private readonly double[] _lapTimes;


        protected Tyre(string name, double slope, double intercept, int totalLaps = 72)
        {
            Name = name;
            Slope = slope;
            Intercept = intercept;
            _lapTimes = new double[totalLaps];

            GenerateLapTimes();
        }

        private void GenerateLapTimes()
        {

            for (int lap = 0; lap < _lapTimes.Length; lap++)
            {
                // using y = mx+c
                _lapTimes[lap] = lap * Slope + Intercept;
            }
        }

        // More efficient for slicing
        public ReadOnlySpan<double> LapTimes => _lapTimes;

    }
    public enum TyreType
    {
        Soft,
        Medium,
        Hard
    }


}