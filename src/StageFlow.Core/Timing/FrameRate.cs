namespace StageFlow.Core.Timing;

/// <summary>MTC's four wire rates. Video output rate is configured separately.</summary>
public enum MtcRate { Fps24, Fps25, Drop2997, Fps30 }

public static class FrameRates
{
    public static int Nominal(this MtcRate rate) => rate switch
    {
        MtcRate.Fps24 => 24, MtcRate.Fps25 => 25,
        MtcRate.Drop2997 or MtcRate.Fps30 => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(rate))
    };
    public static int Numerator(this MtcRate rate) => rate == MtcRate.Drop2997 ? 30000 : rate.Nominal();
    public static int Denominator(this MtcRate rate) => rate == MtcRate.Drop2997 ? 1001 : 1;
    public static double FramesPerSecond(this MtcRate rate) => (double)rate.Numerator() / rate.Denominator();
    public static long FramesPerDay(this MtcRate rate) => rate == MtcRate.Drop2997 ? 2589408 : rate.Nominal() * 86400L;
    public static long ToFrames(this MtcRate rate, TimeSpan time)
    {
        Int128 value = (Int128)time.Ticks * rate.Numerator();
        long divisor = TimeSpan.TicksPerSecond * rate.Denominator();
        // Floor, including negative offsets.
        return checked((long)(value >= 0 ? value / divisor : (value - divisor + 1) / divisor));
    }
    public static TimeSpan ToTime(this MtcRate rate, long frames)
    {
        Int128 ticks = (Int128)frames * TimeSpan.TicksPerSecond * rate.Denominator();
        return TimeSpan.FromTicks(checked((long)(ticks >= 0 ? (ticks + rate.Numerator() - 1) / rate.Numerator() : ticks / rate.Numerator())));
    }
}
