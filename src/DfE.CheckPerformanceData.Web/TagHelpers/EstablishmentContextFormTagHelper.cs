using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Web.TagHelpers;

[HtmlTargetElement("form")]
public sealed class EstablishmentContextFormTagHelper(ImpersonationSessionService? session = null) : TagHelper
{
    public override int Order => 1000;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (HiddenInput(session) is { } input) output.PostContent.AppendHtml(input);
    }

    // The hidden field the impersonation middleware expects on every form POST. Markup that builds
    // its own <form> outside Razor (so this tag helper never sees it) must append this too.
    public static IHtmlContent? HiddenInput(ImpersonationSessionService? session)
    {
        if (session?.CreateStamp() is not { } stamp) return null;
        var input = new TagBuilder("input");
        input.Attributes["type"] = "hidden";
        input.Attributes["name"] = "EstablishmentContext";
        input.Attributes["value"] = stamp;
        return input;
    }
}
