using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AvailabilityTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Search_for_future_dates_returns_seeded_room_types()
    {
        using var client = CreateClient();
        var checkIn = Clock.Today.AddDays(Random.Shared.Next(365, 3650));
        var checkOut = checkIn.AddDays(2);

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = checkIn,
            checkOutDate = checkOut,
            adults = 1,
            children = 0,
            rooms = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(checkIn.ToString("yyyy-MM-dd"), body.GetProperty("checkInDate").GetString());

        var roomTypes = body.GetProperty("roomTypes").EnumerateArray().ToList();
        Assert.NotEmpty(roomTypes);
        Assert.All(roomTypes, rt =>
        {
            Assert.True(rt.GetProperty("available").GetInt32() >= 1);
            Assert.True(rt.GetProperty("capacity").GetInt32() >= 1);
        });
        Assert.Contains(roomTypes, rt => rt.GetProperty("name").GetString() == "Standard");
    }

    [Fact]
    public async Task Search_with_invalid_range_returns_validation_problem()
    {
        using var client = CreateClient();
        var date = Clock.Today.AddDays(30);

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = date,
            checkOutDate = date,
            adults = 1,
            children = 0,
            rooms = 1
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "availability.invalid_range");
    }
}
