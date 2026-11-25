using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>
/// #938: makes the sample rows of one school for one kind of supplier file. A schema names its
/// generator with <c>"x-ingress": { "sample": "&lt;name&gt;" }</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each row is keyed by the schema's property names, with text values, as a CSV row is. Ingress
/// then treats a sample row like a supplier row: it converts the types, stamps INCLUDED and
/// INCLUDABLE, and validates the row against the schema. A row that fails makes the run fail.
/// A key that the schema does not declare is dropped.
/// </para>
/// <para>
/// The rows must be the same on each run for the same school, so get every value from the
/// school, never from a random source or the clock. A school then keeps the same pupils, and the
/// same UPNs, in each release.
/// </para>
/// </remarks>
public interface ISampleDataGenerator
{
    /// <summary>The name that a schema gives in <c>x-ingress.sample</c>. It must not change.</summary>
    string Name { get; }

    /// <param name="school">The school to make rows for.</param>
    /// <param name="schema">The schema of the file. A generator that knows its file can ignore it.</param>
    IEnumerable<IReadOnlyDictionary<string, string>> Rows(SampleSchool school, SampleSchemaShape schema);
}

/// <summary>#938: finds a generator by the name that a schema gives.</summary>
public interface ISampleDataGenerators
{
    /// <summary>The generator with that name, or null.</summary>
    ISampleDataGenerator? Find(string name);
}

/// <inheritdoc cref="ISampleDataGenerators"/>
public sealed class SampleDataGenerators : ISampleDataGenerators
{
    private readonly Dictionary<string, ISampleDataGenerator> _byName;

    public SampleDataGenerators(IEnumerable<ISampleDataGenerator> generators)
    {
        _byName = new Dictionary<string, ISampleDataGenerator>(StringComparer.Ordinal);
        foreach (var generator in generators)
        {
            // Two generators with one name would make the choice depend on the order of registration.
            if (!_byName.TryAdd(generator.Name, generator))
                throw new InvalidOperationException($"Two sample data generators have the name '{generator.Name}'.");
        }
    }

    public ISampleDataGenerator? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Every name, for the tests that check each schema names a generator that exists.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;
}
