namespace DfE.CheckPerformanceData.Web.Extensions;

using System.ComponentModel.DataAnnotations;
using System.Reflection;

public static class EnumExtensions
{
    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();

        return member?
                   .GetCustomAttribute<DisplayAttribute>()?
                   .GetName()
               ?? value.ToString();
    }
}