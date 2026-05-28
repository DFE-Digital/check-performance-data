using System.Reflection;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class HelpControllerHardDeleteTests
{
    private const string ActionName = "DeletePermanently";

    private static MethodInfo GetActionMethod()
    {
        var method = typeof(HelpController).GetMethod(ActionName);
        Assert.NotNull(method);
        return method!;
    }

    // --- DeletePermanently_Has_Authorize_Attribute_With_EditorRole ---

    [Fact]
    public void DeletePermanently_Has_Authorize_Attribute_With_EditorRole()
    {
        var method = GetActionMethod();

        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_content_access_user", authorize!.Roles);
    }

    // --- DeletePermanently_Has_HttpPost_Route_help_delete_permanently_id ---

    [Fact]
    public void DeletePermanently_Has_HttpPost_Route_help_delete_permanently_id()
    {
        var method = GetActionMethod();

        var httpPost = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("help/delete-permanently/{id:int}", httpPost!.Template);
    }

    // --- DeletePermanently_Has_ValidateAntiForgeryToken_Attribute ---

    [Fact]
    public void DeletePermanently_Has_ValidateAntiForgeryToken_Attribute()
    {
        var method = GetActionMethod();

        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
