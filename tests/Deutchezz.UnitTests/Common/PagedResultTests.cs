using Deutchezz.Application.Common;

namespace Deutchezz.UnitTests.Common;

public class PagedResultTests
{
    [Fact]
    public void Envelope_exposes_items_page_pageSize_totalCount_totalPages()
    {
        var result = new PagedResult<string>(["a", "b"], 2, 10, 42);

        Assert.Equal(new[] { "a", "b" }, result.Items);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(42, result.TotalCount);
        Assert.Equal(5, result.TotalPages);
    }

    [Fact]
    public void TotalPages_rounds_up()
    {
        var result = new PagedResult<int>([], 1, 20, 21);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void TotalPages_is_zero_when_empty()
    {
        var result = new PagedResult<int>([], 1, 20, 0);
        Assert.Equal(0, result.TotalPages);
    }
}