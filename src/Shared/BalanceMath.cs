namespace Gatekeeper.Mods;

public static class BalanceMath
{
    public static float Increase(float value, int percent) =>
        IsPositiveFinite(value) ? value * (1f + Clamp(percent, 0, 20) / 100f) : value;

    public static float Shorten(float seconds, int percent) =>
        IsPositiveFinite(seconds) ? seconds * (1f - Clamp(percent, 0, 20) / 100f) : seconds;

    private static bool IsPositiveFinite(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
}
