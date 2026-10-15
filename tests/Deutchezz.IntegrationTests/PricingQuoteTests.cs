using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// Covers the public v1.14 pricing quote (the single source of the billing formula): a normal stay
/// is unchanged, and only the wall-clock time beyond the booked nights is charged a fixed tier.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PricingQuoteTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_normal_single_night_quote_has_no_extra_charge()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var quote = await QuoteAsync(client, roomTypeId, "14:00", 1, "12:00");

        Assert.Equal(1, quote.GetProperty("nights").GetInt32());
        Assert.Equal(0, quote.GetProperty("overageMinutes").GetInt32());
        Assert.Equal(0m, quote.GetProperty("extraChargeAmount").GetDecimal());
        Assert.Equal(30000m, quote.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_two_night_quote_stays_at_two_rates()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // 14:00 -> 12:00 two dates later = 46h, still inside the two booked nights.
        var quote = await QuoteAsync(client, roomTypeId, "14:00", 2, "12:00");

        Assert.Equal(2, quote.GetProperty("nights").GetInt32());
        Assert.Equal(0m, quote.GetProperty("extraChargeAmount").GetDecimal());
        Assert.Equal(60000m, quote.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_four_hour_late_quote_charges_the_first_standard_tier()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // 1 night, 14:00 -> 18:00 next day = 28h = 4h overage -> Standard 2-5h tier = 5,000.
        var quote = await QuoteAsync(client, roomTypeId, "14:00", 1, "18:00");

        Assert.Equal(4 * 60, quote.GetProperty("overageMinutes").GetInt32());
        Assert.Equal(5000m, quote.GetProperty("extraChargeAmount").GetDecimal());
        Assert.Equal(35000m, quote.GetProperty("total").GetDecimal());

        var line = quote.GetProperty("lines")[0];
        Assert.Equal(1, line.GetProperty("nights").GetInt32());
        Assert.Equal(5000m, line.GetProperty("extraCharge").GetDecimal());
        Assert.Equal("2-5h", line.GetProperty("tierLabel").GetString());
        Assert.Equal(35000m, line.GetProperty("lineTotal").GetDecimal());
    }

    [Fact]
    public async Task A_nine_hour_late_quote_charges_the_middle_tier()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // 1 night, 14:00 -> 23:00 next day = 33h = 9h overage -> Standard 5-10h tier = 10,000.
        var quote = await QuoteAsync(client, roomTypeId, "14:00", 1, "23:00");

        Assert.Equal(10000m, quote.GetProperty("extraChargeAmount").GetDecimal());
        Assert.Equal(40000m, quote.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Duplicate_room_types_in_a_quote_are_rejected()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote", new
        {
            checkInDate = Clock.Today.AddDays(30),
            checkOutDate = Clock.Today.AddDays(31),
            rooms = new[]
            {
                new { roomTypeId, quantity = 1 },
                new { roomTypeId, quantity = 1 }
            }
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "quote.duplicate_roomtype");
    }

    [Fact]
    public async Task Extra_charge_is_charged_per_room()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // Two rooms, 1 night, 14:00 -> 18:00 next day = 4h overage. The Standard 2-5h tier is
        // 5,000 per room, so the extra charge must be 10,000 on top of two nightly rates.
        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote", new
        {
            checkInDate = Clock.Today.AddDays(30),
            checkInTime = "14:00",
            checkOutDate = Clock.Today.AddDays(31),
            checkOutTime = "18:00",
            bookingType = "online",
            rooms = new[] { new { roomTypeId, quantity = 2 } }
        });
        response.EnsureSuccessStatusCode();
        var quote = await ReadJsonAsync(response);

        Assert.Equal(4 * 60, quote.GetProperty("overageMinutes").GetInt32());
        Assert.Equal(10000m, quote.GetProperty("extraChargeAmount").GetDecimal());
        Assert.Equal(70000m, quote.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Quote_total_matches_the_created_booking_total()
    {
        // The whole point of the quote endpoint: the number shown before confirming is exactly
        // the number stored on the booking (including any extended-stay charge).
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var checkIn = Clock.Today.AddDays(30);

        var quoteResponse = await client.PostAsJsonAsync("/api/v1/pricing/quote", new
        {
            checkInDate = checkIn,
            checkInTime = "14:00",
            checkOutDate = checkIn.AddDays(1),
            checkOutTime = "18:00",
            bookingType = "online",
            rooms = new[] { new { roomTypeId, quantity = 1 } }
        });
        quoteResponse.EnsureSuccessStatusCode();
        var quote = await ReadJsonAsync(quoteResponse);

        var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = checkIn,
                checkInTime = "14:00",
                checkOutDate = checkIn.AddDays(1),
                checkOutTime = "18:00",
                adults = 1,
                children = 0,
                guest = new
                {
                    firstName = "Ada",
                    lastName = "Obi",
                    email = $"pricing-{Guid.NewGuid():N}@example.com",
                    phoneNumber = UniquePhone()
                },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = (string?)null
            })
        };
        create.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var createResponse = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var booking = (await ReadJsonAsync(createResponse)).GetProperty("booking");
        Assert.Equal(quote.GetProperty("total").GetDecimal(), booking.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(quote.GetProperty("extraChargeAmount").GetDecimal(), booking.GetProperty("extraChargeAmount").GetDecimal());
    }

    private async Task<JsonElement> QuoteAsync(
        HttpClient client, Guid roomTypeId, string checkInTime, int nights, string checkOutTime)
    {
        var checkIn = Clock.Today.AddDays(30);
        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote", new
        {
            checkInDate = checkIn,
            checkInTime,
            checkOutDate = checkIn.AddDays(nights),
            checkOutTime,
            rooms = new[] { new { roomTypeId, quantity = 1 } }
        });

        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }
}
