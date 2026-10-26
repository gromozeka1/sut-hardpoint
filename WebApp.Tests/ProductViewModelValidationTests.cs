using System.ComponentModel.DataAnnotations;
using WebApp.Models;

namespace WebApp.Tests;

/// <summary>
/// Validation tests for <see cref="ProductViewModel"/>: these annotations are what the Razor
/// Create/Edit forms rely on for both client- and server-side validation, and the enum values
/// are part of the JSON contract with ProductAPI.
/// </summary>
public sealed class ProductViewModelValidationTests
{
    private static List<ValidationResult> Validate(ProductViewModel model)
    {
        var results = new List<ValidationResult>();
        _ = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);
        return results;
    }

    private static ProductViewModel ValidProduct() => new()
    {
        Name = "Logitech MX Master 3S",
        Description = "Wireless mouse",
        ProductType = ProductType.PERIPHERALS,
        Price = 99,
        Stock = 3
    };

    [Fact]
    public void ValidProduct_HasNoValidationErrors()
    {
        Assert.Empty(Validate(ValidProduct()));
    }

    [Fact]
    public void EmptyName_FailsRequiredValidation()
    {
        ProductViewModel model = ValidProduct();
        model.Name = string.Empty;

        ValidationResult error = Assert.Single(Validate(model));
        Assert.Contains(nameof(ProductViewModel.Name), error.MemberNames);
    }

    [Fact]
    public void NameLongerThan100Characters_FailsStringLengthValidation()
    {
        ProductViewModel model = ValidProduct();
        model.Name = new string('x', 101);

        Assert.NotEmpty(Validate(model));
    }

    [Fact]
    public void DescriptionLongerThan1000Characters_FailsStringLengthValidation()
    {
        ProductViewModel model = ValidProduct();
        model.Description = new string('x', 1001);

        Assert.NotEmpty(Validate(model));
    }

    [Theory]
    [InlineData(-1, 0, nameof(ProductViewModel.Price))]
    [InlineData(0, -1, nameof(ProductViewModel.Stock))]
    public void NegativePriceOrStock_FailsRangeValidation(int price, int stock, string expectedMember)
    {
        ProductViewModel model = ValidProduct();
        model.Price = price;
        model.Stock = stock;

        List<ValidationResult> errors = Validate(model);
        Assert.NotNull(errors.Find(e => e.ErrorMessage is not null
            && e.ErrorMessage.Contains(expectedMember, StringComparison.Ordinal)));
    }

    [Fact]
    public void ProductType_WireValues_MatchTheApiContract()
    {
        // ProductAPI defines the same numeric values under the (mis)spelling PERIPHARALS;
        // the two enums line up positionally, which is what the JSON payload relies on.
        Assert.Equal(0, (int)ProductType.CPU);
        Assert.Equal(1, (int)ProductType.MONITOR);
        Assert.Equal(2, (int)ProductType.PERIPHERALS);
        Assert.Equal(3, (int)ProductType.EXTERNAL);
    }
}
