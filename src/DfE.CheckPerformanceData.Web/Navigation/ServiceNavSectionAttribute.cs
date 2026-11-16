namespace DfE.CheckPerformanceData.Web.Navigation;

/// <summary>
/// Gives the service navigation section of a controller or an action. An action attribute
/// overrides the controller attribute. A page with no attribute shows no active nav item.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class ServiceNavSectionAttribute(ServiceNavSection section) : Attribute
{
    public ServiceNavSection Section { get; } = section;
}
