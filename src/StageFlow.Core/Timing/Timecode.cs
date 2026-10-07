using System.Globalization;

namespace StageFlow.Core.Timing;

public readonly record struct Timecode(int Hours, int Minutes, int Seconds, int Frames, MtcRate Rate)
{
    public bool IsValid => (uint)Rate <= 3 && (uint)Hours < 24 && (uint)Minutes < 60 &&
        (uint)Seconds < 60 && (uint)Frames < Rate.Nominal() &&
        !(Rate == MtcRate.Drop2997 && Minutes % 10 != 0 && Seconds == 0 && Frames < 2);

    public long ToFrameNumber()
    {
        if (!IsValid) throw new ArgumentException("Invalid timecode label.");
        long frames = ((Hours * 60L + Minutes) * 60 + Seconds) * Rate.Nominal() + Frames;
        int minutes = Hours * 60 + Minutes;
        return frames - (Rate == MtcRate.Drop2997 ? 2 * (minutes - minutes / 10) : 0);
    }

    public static Timecode FromFrameNumber(long frames, MtcRate rate)
    {
        long day = rate.FramesPerDay();
        frames = ((frames % day) + day) % day;
        if (rate == MtcRate.Drop2997)
        {
            long tens = frames / 17982;
            long remainder = frames % 17982;
            frames += 18 * tens + (remainder >= 2 ? 2 * ((remainder - 2) / 1798) : 0);
        }
        int fps = rate.Nominal();
        return new((int)(frames / (fps * 3600)), (int)(frames / (fps * 60) % 60),
            (int)(frames / fps % 60), (int)(frames % fps), rate);
    }

    public Timecode AddFrames(long frames) => FromFrameNumber(ToFrameNumber() + frames, Rate);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Hours:00}:{Minutes:00}:{Seconds:00}{(Rate == MtcRate.Drop2997 ? ';' : ':')}{Frames:00}");

    public static bool TryParse(string? text, MtcRate rate, out Timecode result)
    {
        result = default;
        if (text is null || (uint)rate > 3) return false;
        var parts = text.Trim().Replace(';', ':').Split(':');
        if (parts.Length != 4) return false;
        Span<int> values = stackalloc int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
        result = new(values[0], values[1], values[2], values[3], rate);
        return result.IsValid;
    }
}
