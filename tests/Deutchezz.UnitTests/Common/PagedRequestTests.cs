using Deutchezz.Application.Common;

namespace Deutchezz.UnitTests.Common;

public class PagedRequestTests
{
    [Fact]
    public void Defaults_are_page_1_and_pageSize_20()
    {
        var request = new PagedRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(0, request.Offset);
        Assert.False(request.IsDescending);
    }

    [Fact]
    public void Page_below_1_clamps_to_1()
    {
        var request = new PagedRequest { Page = 0 };
        Assert.Equal(1, request.Page);

        request.Page = -5;
        Assert.Equal(1, request.Page);
        Assert.Equal(0, request.Offset);
    }

    [Fact]
    public void PageSize_above_100_clamps_to_100()
    {
        var request = new PagedRequest { PageSize = 500 };
        Assert.Equal(100, request.PageSize);
    }

    [Fact]
    public void PageSize_below_1_falls_back_to_default_20()
    {
        var request = new PagedRequest { PageSize = 0 };
        Assert.Equal(PagedRequest.DefaultPageSize, request.PageSize);

        request.PageSize = -3;
        Assert.Equal(PagedRequest.DefaultPageSize, request.PageSize);
    }

    [Theory]
    [InlineData("desc", true)]
    [InlineData("DESC", true)]
    [InlineData("asc", false)]
    [InlineData(null, false)]
    public void Direction_is_case_insensitive(string? direction, bool expected)
    {
        var request = new PagedRequest { Direction = direction };
        Assert.Equal(expected, request.IsDescending);
    }

    [Fact]
    public void Offset_computed_from_page_and_page_size()
    {
        var request = new PagedRequest { Page = 4, PageSize = 25 };
        Assert.Equal(75, request.Offset);
    }
}