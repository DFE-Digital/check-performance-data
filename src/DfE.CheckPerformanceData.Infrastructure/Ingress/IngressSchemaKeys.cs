namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The keys of the <c>x-ingress</c> schema extension that ingress reads. A supplier schema names
/// them, so a value must not change.
/// </summary>
internal static class IngressSchemaKeys
{
    public const string Extension = "x-ingress";

    public const string Inclusion = "inclusion";
    public const string Includable = "includable";
    public const string CodeDescriptions = "codeDescriptions";
    public const string InstitutionKey = "institutionKey";
    public const string UrnKey = "urnKey";

    /// <summary>#938: the name of the generator that makes the sample rows of a school.</summary>
    public const string Sample = "sample";

    public const string Source = "source";
    public const string Separator = "separator";

    public const string Field = "field";
    public const string Included = "included";
    public const string Codes = "codes";
    public const string DescriptionField = "descriptionField";
    public const string Descriptions = "descriptions";

    public const string InclusionPath = Extension + "." + Inclusion;
    public const string IncludablePath = Extension + "." + Includable;
    public const string CodeDescriptionsPath = Extension + "." + CodeDescriptions;
    public const string SamplePath = Extension + "." + Sample;
}
