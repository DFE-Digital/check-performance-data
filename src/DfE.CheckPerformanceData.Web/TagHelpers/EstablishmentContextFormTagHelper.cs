using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Web.TagHelpers;

[HtmlTargetElement("form")]
public sealed class EstablishmentContextFormTagHelper(ImpersonationSessionService? session = null) : TagHelper
{
    public override int Order => 1000;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (session?.CreateStamp() is not { } stamp) return;
        var input = new TagBuilder("input");
        input.Attributes["type"] = "hidden";
        input.Attributes["name"] = "EstablishmentContext";
        input.Attributes["value"] = stamp;
        output.PostContent.AppendHtml(input);
    }
}
