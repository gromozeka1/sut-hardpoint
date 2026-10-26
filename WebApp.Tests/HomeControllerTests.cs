using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebApp.Controllers;
using WebApp.Models;

namespace WebApp.Tests;

/// <summary>
/// Unit tests for the template-provided <see cref="HomeController"/> routes and the
/// <see cref="ErrorViewModel"/> flag used by the Error view.
/// </summary>
public sealed class HomeControllerTests
{
    private static HomeController CreateController(string traceIdentifier = "trace-123")
    {
        DefaultHttpContext httpContext = new() { TraceIdentifier = traceIdentifier };
        return new HomeController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public void Index_RendersDefaultView()
    {
        ViewResult view = Assert.IsType<ViewResult>(CreateController().Index());

        Assert.Null(view.ViewName);
        Assert.Null(view.Model);
    }

    [Fact]
    public void Privacy_RendersDefaultView()
    {
        ViewResult view = Assert.IsType<ViewResult>(CreateController().Privacy());

        Assert.Null(view.ViewName);
        Assert.Null(view.Model);
    }

    [Fact]
    public void Error_RendersViewWithTraceIdentifierAsRequestId()
    {
        ViewResult view = Assert.IsType<ViewResult>(CreateController("trace-42").Error());

        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("trace-42", model.RequestId);
        Assert.True(model.ShowRequestId);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("req-1", true)]
    public void ShowRequestId_ReflectsWhetherARequestIdIsPresent(string? requestId, bool expected)
    {
        ErrorViewModel model = new() { RequestId = requestId };

        Assert.Equal(expected, model.ShowRequestId);
    }
}
