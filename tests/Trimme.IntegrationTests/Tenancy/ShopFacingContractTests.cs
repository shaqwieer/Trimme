using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Trimme.BuildingBlocks.Web.Security;
using Trimme.IntegrationTests.Identity;
using Trimme.IntegrationTests.Infrastructure;
using Trimme.Modules.Identity.Domain;

namespace Trimme.IntegrationTests.Tenancy;

/// <summary>
/// R-NEG-04 framework: no shop-facing response may carry customer contact data. Every endpoint a shop user can call is
/// found from its metadata; each must declare its response types (fail closed), none of those types may contain a
/// phone-like member, and every such GET endpoint's live JSON is scanned for phone numbers. Later phases' shop
/// endpoints are covered automatically.
/// </summary>
public sealed partial class ShopFacingContractTests(PostgresFixture postgres)
{
    /// <summary>Reviewed phone-named members that are not customer data. Adding one is a reviewed decision.</summary>
    private static readonly Dictionary<string, string> AllowedMembers = new(StringComparer.Ordinal)
    {
        ["ShopOwnProfileResponse.PublicPhone"] = "The shop's own public business number, which the shop itself edits (Phase 06).",
    };

    [Fact]
    public async Task ShopFacingContracts_DoNotContainCustomerPhone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ShopTestData.CreateFactoryAsync(postgres, "shop_contracts", ct);
        var endpoints = ShopFacingEndpoints(factory);
        endpoints.ShouldNotBeEmpty();

        var violations = new List<string>();
        foreach (var (key, endpoint) in endpoints)
        {
            var responseTypes = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
                .Where(m => m.StatusCode is >= 200 and < 300 && m.Type is not null && m.Type != typeof(void))
                .Select(m => m.Type!)
                .ToArray();
            var returnsNothing = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Any(m => m.StatusCode == 204);
            if (responseTypes.Length == 0 && !returnsNothing)
            {
                violations.Add($"{key}: declares no response type, so its contract cannot be checked (add Produces<T>).");
            }

            foreach (var type in responseTypes)
            {
                violations.AddRange(PhoneLikeMembers(type, type.Name, [])
                    .Where(member => !AllowedMembers.ContainsKey(member))
                    .Select(member => $"{key}: {member}"));
            }
        }

        violations.ShouldBeEmpty();

        // Live payloads: every shop-facing GET, called as a shop owner, contains no phone number.
        using var admin = await IdentityTestData.SignInNewStaffAsync(factory, SystemRoles.SuperAdmin, ct);
        var shops = await ShopTestData.CreateTwoShopsAsync(factory, admin, ct);
        using var owner = await IdentityTestData.SignInStaffAsync(factory, shops.A.OwnerEmail, ct);
        foreach (var (key, _) in endpoints.Where(e => e.Key.StartsWith("GET ", StringComparison.Ordinal) && !e.Key.Contains('{', StringComparison.Ordinal)))
        {
            using var response = await owner.GetAsync(key[4..], ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            PhoneInText().IsMatch(body).ShouldBeFalse($"{key} returned a phone-like value: {body}");
        }
    }

    [Fact]
    public void PhoneScanner_FindsPhoneMembers_InNestedTypes()
    {
        // Non-vacuity: the scanner must catch a phone buried in a nested collection.
        PhoneLikeMembers(typeof(ProbeBooking), nameof(ProbeBooking), []).ShouldBe(["ProbeBooking.Customers.MobileNumber"]);
        PhoneInText().IsMatch("""{"customer":"+966 50 214 8830"}""").ShouldBeTrue();
        PhoneInText().IsMatch("""{"customer":"0502148830"}""").ShouldBeTrue();
    }

    private static List<(string Key, RouteEndpoint Endpoint)> ShopFacingEndpoints(TrimmeApiFactory factory) =>
        [.. factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiredUserTypeMetadata>()?.UserType == UserTypes.ShopUser
                        || e.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission.StartsWith("Shop.", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => ($"{method} /{e.RoutePattern.RawText!.TrimStart('/')}", e)))];

    private static IEnumerable<string> PhoneLikeMembers(Type type, string path, HashSet<Type> seen)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTimeOffset)
            || type == typeof(decimal) || type.IsEnum || !seen.Add(type))
        {
            yield break;
        }

        var element = type.IsArray ? type.GetElementType()
            : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments()[^1]
            : null;
        if (element is not null)
        {
            foreach (var nested in PhoneLikeMembers(element, path, seen))
            {
                yield return nested;
            }

            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = $"{path}.{property.Name}";
            if (PhoneName().IsMatch(property.Name))
            {
                yield return propertyPath;
            }

            foreach (var nested in PhoneLikeMembers(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, propertyPath, seen))
            {
                yield return nested;
            }
        }
    }

    [GeneratedRegex("phone|mobile|msisdn|whatsapp|contactnumber", RegexOptions.IgnoreCase)]
    private static partial Regex PhoneName();

    // +966 5x…, 9665…, 05… with optional separators; Latin or Arabic-Indic digits.
    [GeneratedRegex(@"(\+|00)?9\s?6\s?6[\s-]?5(?:[\s-]?[0-9٠-٩]){8}|\b0\s?5(?:[\s-]?[0-9٠-٩]){8}\b")]
    private static partial Regex PhoneInText();

    private sealed record ProbeBooking(Guid Id, IReadOnlyList<ProbeCustomer> Customers)
    {
        public ProbeCustomer? Customer { get; init; }
    }

    private sealed record ProbeCustomer(string Name, string MobileNumber);
}
