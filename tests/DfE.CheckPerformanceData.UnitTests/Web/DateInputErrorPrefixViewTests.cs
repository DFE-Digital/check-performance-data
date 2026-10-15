using System.Reflection;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using GovUk.Frontend.AspNetCore;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// GovUk.Frontend.AspNetCore's <govuk-date-input> throws at render time when it has no error
// message prefix, from either an error-message-prefix attribute or the bound property's
// [DateInput(ErrorMessagePrefix = ...)]. A missing prefix compiles and only fails when the page
// is opened: "Add checking exercise" failed that way on preproduction (VisibleFrom/VisibleUntil).
// Same hostless pattern as AccessibilityAuditViewTests: read the .cshtml as text.
public sealed partial class DateInputErrorPrefixViewTests
{
	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	private static string ViewsRoot()
	{
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
		return Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views");
	}

	[GeneratedRegex(@"^@model\s+([\w.]+)", RegexOptions.Multiline)]
	private static partial Regex ModelDirective();

	[GeneratedRegex(@"<govuk-date-input\s([^>]*)>")]
	private static partial Regex DateInputTag();

	[GeneratedRegex(@"\bfor=""(?:@Model\.)?([\w.]+)""")]
	private static partial Regex ForAttribute();

	[Fact]
	public void EveryDateInput_HasAnErrorMessagePrefix()
	{
		var webAssembly = typeof(AdminPage).Assembly;
		var failures = new List<string>();
		var checkedCount = 0;

		foreach (var file in Directory.EnumerateFiles(ViewsRoot(), "*.cshtml", SearchOption.AllDirectories))
		{
			var source = File.ReadAllText(file);
			var name = Path.GetRelativePath(ViewsRoot(), file);

			foreach (Match tag in DateInputTag().Matches(source))
			{
				var attributes = tag.Groups[1].Value;
				if (attributes.Contains("error-message-prefix=", StringComparison.Ordinal))
					continue;

				var modelMatch = ModelDirective().Match(source);
				var forMatch = ForAttribute().Match(attributes);
				if (!modelMatch.Success || !forMatch.Success)
				{
					failures.Add($"{name}: cannot resolve the model property of <govuk-date-input {attributes}>");
					continue;
				}

				var property = ResolveProperty(webAssembly, modelMatch.Groups[1].Value, forMatch.Groups[1].Value);
				checkedCount++;
				if (property is null)
					failures.Add($"{name}: no property {forMatch.Groups[1].Value} on {modelMatch.Groups[1].Value}");
				else if (string.IsNullOrWhiteSpace(property.GetCustomAttribute<DateInputAttribute>()?.ErrorMessagePrefix))
					failures.Add($"{name}: {property.DeclaringType!.Name}.{property.Name} has no [DateInput(ErrorMessagePrefix = ...)]");
			}
		}

		Assert.True(checkedCount > 0, "The scan found no date inputs, so it checks nothing.");
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	private static PropertyInfo? ResolveProperty(Assembly assembly, string modelTypeName, string path)
	{
		var type = assembly.GetType(modelTypeName);
		PropertyInfo? property = null;
		foreach (var segment in path.Split('.'))
		{
			if (type is null)
				return null;
			property = type.GetProperty(segment);
			type = property?.PropertyType;
		}
		return property;
	}
}
