namespace F1_simulation.Tyres;

abstract class Tyre
{
    protected double Slope { get; }
    protected double Intercept { get; }
    public string Name { get; }
    protected  double[] Lap_times { get; }

    protected Tyre(string name, double slope, double intercept)
    {
        Name = name;
        Slope = slope;
        Intercept = intercept;
        Lap_times = generate_lap_times();
    }

    private double[] generate_lap_times(int total_laps = 60)
    {
        var times = new double[total_laps];
        for (int lap = 0; lap < total_laps; lap++)
        {
            times[lap] = lap * Slope + Intercept;
        }
        return times;
    }

    
}