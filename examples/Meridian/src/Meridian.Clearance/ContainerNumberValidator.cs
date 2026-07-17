namespace Meridian.Clearance;

public sealed class ContainerNumberValidator
{
    public bool IsValid(string containerNumber)
    {
        if (string.IsNullOrEmpty(containerNumber) || containerNumber.Length != 11) return false;

        for (var i = 0; i < 4; i++)
            if (!char.IsAsciiLetterUpper(containerNumber[i]))
                return false;

        if (containerNumber[3] != 'U') return false;

        for (var i = 4; i < 10; i++)
            if (!char.IsAsciiDigit(containerNumber[i]))
                return false;

        char checkChar = containerNumber[10];
        if (!char.IsAsciiDigit(checkChar)) return false;

        int expected = ContainerCheckDigit.Compute(containerNumber[..10]);
        return checkChar - '0' == expected;
    }
}