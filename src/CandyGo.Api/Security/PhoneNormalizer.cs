namespace CandyGo.Api.Security;

public static class PhoneNormalizer
{
    public static string Normalize(string rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            throw new ArgumentException("Teléfono requerido.", nameof(rawPhone));
        }

        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
        {
            throw new ArgumentException("Teléfono inválido.", nameof(rawPhone));
        }

        return digits;
    }
}
