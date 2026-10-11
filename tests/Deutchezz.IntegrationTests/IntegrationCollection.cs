namespace Deutchezz.IntegrationTests;

/// <summary>
/// Shares one API host (and therefore one migrated database) across every integration test
/// class. Sharing also serializes the classes, so migrations/seeding run exactly once and
/// tests never race on the same database.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<HotelWebApplicationFactory>
{
    public const string Name = "integration";
}
