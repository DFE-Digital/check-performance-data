namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for Views/Shared/_Layout.cshtml — specifically the
// sign-in / sign-out cluster that replaces the old pink dev-impersonate pill. Pattern
// mirrors SearchViewRenderTests: read the .cshtml as text and assert on the source.
// This keeps the suite hostless (no MVC test harness, no new NuGet) while still
// catching the most likely regressions — accidental removal of a branch, wrong
// endpoint URL, or the dev dropdown leaking into production-rendered output.
//
// Behaviour each test pins:
//   - Anonymous nav shows "Sign in" + caret dropdown (non-prod only)
//   - Real DfE auth flips text to "Sign out" with optional name and (impersonating CMS admin) suffix
//   - Sign-out target comes from SignOutLink (see SignOutLinkTests)
//   - hasRealDfeAuth uses "any non-impersonation identity" — not a hard-coded "Cookies"
//     scheme match (the regression where DfE Sign-In auth wasn't detected)
//   - The pink dev-impersonate pill is gone
public sealed class LayoutRenderTests
{
	private static string ReadLayout()
	{
		// Use the .cs file's path via CallerFilePath rather than AppContext.BaseDirectory
		// so the test works regardless of where the test binary is dropped (in-tree
		// bin/Debug/... or out-of-tree via `dotnet test -o ...`). That .cs file lives at
		// {repo}/tests/DfE.CheckPerformanceData.UnitTests/Web/LayoutRenderTests.cs, so
		// the repo root is three levels up; the layout sits under src/.../Views/Shared.
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var layout = Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_Layout.cshtml");
		return File.ReadAllText(layout);
	}

	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	[Fact]
	public void Layout_HasRealDfeAuth_DoesNotHardcodeCookieScheme()
	{
		// Regression: previously compared AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme,
		// which is "Cookies". DfE Sign-In's pipeline adds an enrichment identity ("DfeSignIn")
		// and the primary identity's AuthenticationType can be either, so the equality
		// check missed the real-auth case. Current logic must check "any authenticated
		// non-impersonation identity".
		var view = ReadLayout();
		Assert.DoesNotContain("CookieAuthenticationDefaults.AuthenticationScheme", view);
		Assert.Contains("User.Identities.Any", view);
		Assert.Contains("DevImpersonationConstants.Scheme", view);
	}

	[Fact]
	public void Layout_DetectsEditorImpersonationByCookieValue()
	{
		var view = ReadLayout();
		// Pins the cookie-name + EditorValue comparison so a refactor of either constant
		// rename trips a test instead of silently breaking the nav.
		Assert.Contains("DevImpersonationConstants.CookieName", view);
		Assert.Contains("DevImpersonationConstants.EditorValue", view);
	}

	[Fact]
	public void Layout_SignOutHref_ComesFromSignOutLink()
	{
		// AB#298317: the real-auth vs impersonation decision moved into SignOutLink so Check your
		// pupil data can sign a school out the same way. SignOutLinkTests pins the two targets.
		var view = ReadLayout();
		Assert.Contains("SignOutLink.For(Context, Url)", view);
		Assert.DoesNotContain("Url.Action(\"DfeSignOut\", \"Account\")", view);
	}

	[Fact]
	public void Layout_SignOutHref_ForImpersonationOnly_ClearsCookie()
	{
		// Regression: sign-out for impersonation-only used to point at /dev/impersonate/user,
		// which kept a synthetic "Dev impersonation user" principal authenticated. The decision now
		// lives in SignOutLink; the layout must not reintroduce its own fallback.
		var view = ReadLayout();
		Assert.DoesNotContain(": \"/dev/impersonate/user\"", view);
		Assert.DoesNotContain(": \"/dev/impersonate/clear\"", view);
	}

	[Fact]
	public void Layout_NameInParens_PullsGivenNameAndSurnameClaims()
	{
		var view = ReadLayout();
		Assert.Contains("ClaimTypes.GivenName", view);
		Assert.Contains("ClaimTypes.Surname", view);
		// Fall-through to Identity.Name if the explicit claims are missing.
		Assert.Contains("User.Identity?.Name", view);
	}

	[Fact]
	public void Layout_SignOutLabel_IncludesImpersonatingSuffix()
	{
		var view = ReadLayout();
		// Editor cookie → "impersonating CMS editor"; admin cookie → "impersonating CMS administrator".
		// The suffixes are role-specific so a viewer can tell at a glance which synthetic
		// principal is in play.
		Assert.Contains("impersonating CMS editor", view);
		Assert.Contains("impersonating CMS administrator", view);
		// The label assembles parts with ", " — pins the join separator so the combined
		// "Sign out (Lance Keay, impersonating CMS editor)" output stays consistent.
		Assert.Contains("\", \"", view);
	}

	[Fact]
	public void Layout_SignInDropdown_GatedOnNonProduction()
	{
		// Pins the !IsProduction() guard so the "As CMS admin" affordance can never
		// leak into prod-rendered output, even if other branch conditions evolve.
		var view = ReadLayout();
		Assert.Contains("!HostEnvironment.IsProduction()", view);
		Assert.Contains("showImpersonateDropdown", view);
	}

	[Fact]
	public void Layout_SignInDropdown_TargetsImpersonateEditorEndpoint()
	{
		var view = ReadLayout();
		Assert.Contains("/dev/impersonate/editor", view);
		Assert.Contains("As CMS Editor", view);
	}

	[Fact]
	public void Layout_SignInDropdown_TargetsImpersonateAdminEndpoint()
	{
		var view = ReadLayout();
		Assert.Contains("/dev/impersonate/admin", view);
		Assert.Contains("As CMS Admin", view);
	}

	[Fact]
	public void Layout_SignInDropdown_HasCaretToggleAndPanel()
	{
		var view = ReadLayout();
		// Markup hooks the dropdown JS in site.js looks for.
		Assert.Contains("sign-in-nav-item__toggle", view);
		Assert.Contains("sign-in-dropdown-menu", view);
		Assert.Contains("aria-expanded=\"false\"", view);
		Assert.Contains("aria-controls=\"sign-in-dropdown-menu\"", view);
		// Tooltip text on the As CMS Admin menu item.
		Assert.Contains("Take on the admin role for this session.", view);
	}

	[Fact]
	public void Layout_OldPinkImpersonatePill_IsRemoved()
	{
		// The old dev-impersonate-item class + its labels ("admin"/"anon") and lightning
		// bolt pill styling should be fully gone — they were the visual the user asked
		// to retire.
		var view = ReadLayout();
		Assert.DoesNotContain("dev-impersonate-item", view);
		Assert.DoesNotContain(">admin<", view);
		Assert.DoesNotContain(">anon<", view);
	}

	[Fact]
	public void Layout_ServiceNavigation_LinksToGuidance_NotHelp()
	{
		// The old service-nav link pointed at /help with the text "Help"; the CMS-migrated
		// help content lives under /guidance now, so the header link is updated to match.
		// Pins BOTH the retired href/text (so a regression can't quietly re-introduce it)
		// AND the new /guidance href + "Guidance" text.
		var view = ReadLayout();

		Assert.DoesNotContain("href=\"/help\"", view);
		Assert.DoesNotContain(">Help</a>", view);

		Assert.Contains("href=\"/guidance\"", view);
		Assert.Contains(">Guidance</a>", view);
	}

	[Fact]
	public void Layout_ServiceNavigation_StillReportsAsGdsServiceNavigation()
	{
		// We replaced the <govuk-service-navigation> tag helper with hand-rolled markup
		// to host the split-button. Pin the GDS-compatible classes and data-module so a
		// future "tidy" doesn't accidentally drop them and break GOV.UK Frontend JS init.
		var view = ReadLayout();
		Assert.Contains("data-module=\"govuk-service-navigation\"", view);
		Assert.Contains("govuk-service-navigation__list", view);
		Assert.Contains("govuk-service-navigation__item", view);
		Assert.Contains("govuk-service-navigation__link", view);
	}
}
