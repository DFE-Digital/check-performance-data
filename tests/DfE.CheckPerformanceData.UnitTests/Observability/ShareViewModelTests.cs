using System.Collections;
using System.Reflection;
using DfE.CheckPerformanceData.Web.Models.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The anonymised share link and the wallboard are the security-sensitive surfaces of the
// pipeline observability work: they are viewable without the admin role, gated only by an
// opaque token. The defence against a pupil-data leak is absence-by-construction — the share
// view model is a SEPARATE aggregate-only type that carries no pupil-bearing property at all,
// not redaction-at-render on the authenticated dashboard model. These tests reflect over the
// type's whole property graph and fail if any property name looks like a pupil identifier, and
// assert the type is distinct from the authenticated DashboardViewModel.
public sealed class ShareViewModelTests
{
    // Any property whose name contains one of these (case-insensitive) is treated as a
    // pupil-identifier leak. Aggregate concepts (counts, rates, percentages, stage labels,
    // health, a queue *name*) carry none of these. The terms are the specific pupil-identifying
    // ones — "surname"/"forename" rather than a bare "name" so a legitimate QueueName is not a
    // false positive.
    private static readonly string[] PupilIdentifierTerms =
    {
        "upn",
        "uln",
        "pupil",
        "surname",
        "forename",
        "firstname",
        "lastname",
        "fullname",
        "dateofbirth",
        "referencenumber",
        "payload",
        "requester",
        "email",
        "address",
        "postcode",
    };

    // --- The aggregate share view model exposes no pupil-identifier property anywhere in its graph ---

    [Fact]
    public void AggregateShareViewModel_ExposesNoPupilIdentifierProperties()
    {
        var offenders = new List<string>();
        CollectOffendingProperties(typeof(AggregateShareViewModel), new HashSet<Type>(), "AggregateShareViewModel", offenders);

        Assert.True(
            offenders.Count == 0,
            "The aggregate share view model must carry no pupil-identifier property by construction. Offending properties: "
                + string.Join(", ", offenders));
    }

    // --- It is a distinct type from the authenticated dashboard model (not reuse-and-redact) ---

    [Fact]
    public void AggregateShareViewModel_IsNotTheAuthenticatedDashboardViewModel()
    {
        Assert.NotEqual(typeof(DashboardViewModel), typeof(AggregateShareViewModel));
        Assert.False(typeof(DashboardViewModel).IsAssignableFrom(typeof(AggregateShareViewModel)));
        Assert.False(typeof(AggregateShareViewModel).IsAssignableFrom(typeof(DashboardViewModel)));
    }

    private static void CollectOffendingProperties(
        Type type,
        ISet<Type> visited,
        string path,
        List<string> offenders)
    {
        if (!visited.Add(type))
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = $"{path}.{property.Name}";

            if (PupilIdentifierTerms.Any(term =>
                    property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                offenders.Add(propertyPath);
            }

            foreach (var nested in NestedAggregateTypes(property.PropertyType))
            {
                CollectOffendingProperties(nested, visited, propertyPath, offenders);
            }
        }
    }

    // Walks into nested complex types (and the element type of any collection) so the denylist
    // is enforced over the whole graph, not just the top level.
    private static IEnumerable<Type> NestedAggregateTypes(Type type)
    {
        if (IsLeaf(type))
            yield break;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (!IsLeaf(arg))
                    yield return arg;
            }

            yield break;
        }

        if (type.IsArray)
        {
            var element = type.GetElementType();
            if (element is not null && !IsLeaf(element))
                yield return element;

            yield break;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
            yield break;

        yield return type;
    }

    // Primitives, strings, value types and the BCL temporal/numeric types carry no further
    // nested properties worth walking.
    private static bool IsLeaf(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }
}
