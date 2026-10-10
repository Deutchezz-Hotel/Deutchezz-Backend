using System.Security.Cryptography;
using Deutchezz.Application.Abstractions;

namespace Deutchezz.Infrastructure.Services;

public class BookingReferenceGenerator(IBusinessClock clock) : IBookingReferenceGenerator
{
    // Unambiguous alphabet: no 0/O, 1/I/L.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Generate()
    {
        var year = clock.Today.Year;
        var suffix = new char[6];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"DHI-{year}-{new string(suffix)}";
    }
}