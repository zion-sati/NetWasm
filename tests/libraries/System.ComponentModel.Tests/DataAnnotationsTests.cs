// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// System.ComponentModel.DataAnnotations: each attribute validates a value itself, with upstream's messages, and
// Validator validates a value against the attributes it is given, on NetWasm as on desktop .NET 11. Validating an
// object or a property finds their attributes by reflection, which NetWasm does not support: it throws
// PlatformNotSupportedException there, and works on desktop.
public sealed class DataAnnotationsTests
{
    private static IEnumerable<(ValidationAttribute Attribute, object? Value, bool Valid)> Cases()
    {
        yield return (new RequiredAttribute(), "x", true);
        yield return (new RequiredAttribute(), "", false);
        yield return (new RequiredAttribute { AllowEmptyStrings = true }, "", true);
        yield return (new RequiredAttribute(), null, false);
        yield return (new RangeAttribute(1, 10), 5, true);
        yield return (new RangeAttribute(1, 10), 11, false);
        yield return (new RangeAttribute(0.5, 1.5) { MinimumIsExclusive = true }, 0.5, false);
        yield return (new StringLengthAttribute(3) { MinimumLength = 2 }, "ab", true);
        yield return (new StringLengthAttribute(3), "abcd", false);
        yield return (new MinLengthAttribute(2), new[] { 1 }, false);
        yield return (new MaxLengthAttribute(2), new List<int> { 1, 2 }, true);
        yield return (new MaxLengthAttribute(2), "abc", false);
        yield return (new LengthAttribute(1, 2), new[] { 1, 2, 3 }, false);
        yield return (new EmailAddressAttribute(), "someone@example.com", true);
        yield return (new EmailAddressAttribute(), "someone", false);
        yield return (new PhoneAttribute(), "+1 (555) 010-0100", true);
        yield return (new PhoneAttribute(), "phone", false);
        yield return (new UrlAttribute(), "https://example.com/", true);
        yield return (new UrlAttribute(), "example.com", false);
        yield return (new CreditCardAttribute(), "4111 1111 1111 1111", true);
        yield return (new CreditCardAttribute(), "4111 1111 1111 1112", false);
        yield return (new RegularExpressionAttribute("^[a-z]+$"), "abc", true);
        yield return (new RegularExpressionAttribute("^[a-z]+$"), "ab1", false);
        yield return (new AllowedValuesAttribute("red", "green"), "green", true);
        yield return (new DeniedValuesAttribute("red", "green"), "green", false);
        yield return (new Base64StringAttribute(), "QUJD", true);
        yield return (new Base64StringAttribute(), "not base64", false);
        yield return (new FileExtensionsAttribute { Extensions = "png,jpg" }, "photo.JPG", true);
        yield return (new EnumDataTypeAttribute(typeof(DayOfWeek)), DayOfWeek.Friday, true);
        yield return (new EnumDataTypeAttribute(typeof(DayOfWeek)), (DayOfWeek)12, false);
    }

    [Test]
    public async Task EachAttributeValidatesAValueItself()
    {
        var wrong = Cases().Select((item, index) => (item, index))
            .Where(entry => entry.item.Attribute.IsValid(entry.item.Value) != entry.item.Valid)
            .Select(entry => entry.index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(string.Join("; ", wrong)).IsEqualTo("");
    }

    [Test]
    public async Task AValueValidatesAgainstTheAttributesGivenWithUpstreamsMessages()
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(new object()) { DisplayName = "Name", MemberName = "Name" };

        var valid = Validator.TryValidateValue("x", context, results,
            [new StringLengthAttribute(5) { MinimumLength = 2 }, new RegularExpressionAttribute("^[0-9]+$")]);

        await Assert.That(valid).IsFalse();
        await Assert.That(string.Join("|", results.Select(result => result.ErrorMessage))).IsEqualTo(
            "The field Name must be a string with a minimum length of 2 and a maximum length of 5.|" +
            "The field Name must match the regular expression '^[0-9]+$'.");
        await Assert.That(results[0].MemberNames.Single()).IsEqualTo("Name");
        await Assert.That(new RangeAttribute(1, 10).FormatErrorMessage("Count")).IsEqualTo("The field Count must be between 1 and 10.");
        await Assert.That(new RequiredAttribute { ErrorMessage = "{0} is needed" }.FormatErrorMessage("Id")).IsEqualTo("Id is needed");
        var exception = Assert.Throws<ValidationException>(() => Validator.ValidateValue(null, context, [new RequiredAttribute()]));
        await Assert.That(exception.Message).IsEqualTo("The Name field is required.");
    }

    [Test]
    public async Task ValidatingAnObjectFindsItsAttributesByReflection()
    {
        var order = new Order { Quantity = 0 };
        var context = new ValidationContext(order);
        var results = new List<ValidationResult>();
#if DESKTOP_ORACLE
        await Assert.That(Validator.TryValidateObject(order, context, results, validateAllProperties: true)).IsFalse();
        await Assert.That(results.Count).IsEqualTo(2);
#else
        var exception = Assert.Throws<PlatformNotSupportedException>(() =>
            Validator.TryValidateObject(order, context, results, validateAllProperties: true));
        await Assert.That(exception.Message).Contains("Validator.TryValidateValue");
        Assert.Throws<PlatformNotSupportedException>(() => Validator.ValidateProperty(0, new ValidationContext(order) { MemberName = "Quantity" }));
        await Assert.That(async () => await Validator.TryValidateObjectAsync(order, context, results)).Throws<PlatformNotSupportedException>();
#endif
    }

    private sealed class Order
    {
        [Required]
        public string? Customer { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; }
    }
}
