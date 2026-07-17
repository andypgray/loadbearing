namespace Meridian.Clearance;

internal static class ContainerCheckDigit
{
    // ISO 6346 letter values (A-Z), every multiple of 11 skipped.
    private static readonly int[] LetterValues =
    [
        10, 12, 13, 14, 15, 16, 17, 18, 19, 20,
        21, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 34, 35, 36, 37, 38
    ];

    internal static int Compute(string first10)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            char c = first10[i];
            int value = char.IsAsciiDigit(c) ? c - '0' : LetterValues[c - 'A'];
            sum += value << i;
        }

        int remainder = sum % 11;
        return remainder == 10 ? 0 : remainder;
    }
}