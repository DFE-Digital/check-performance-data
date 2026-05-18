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
//   - Sign-out target is /Secret/DfeSignOut for real auth and /dev/impersonate/clear for
//     impersonation-only (NOT /user — that was the regression the user reported)
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
	public void Layout_SignOutHref_GoesToDfeSignOut_ForRealAuth()
	{
		var view = ReadLayout();
		Assert.Contains("Url.Action(\"DfeSignOut\", \"Secret\")", view);
	}

	[Fact]
	public void Layout_SignOutHref_ForImpersonationOnly_ClearsCookie()
	{
		// Regression: sign-out for impersonation-only used to point at /dev/impersonate/user,
		// which kept a synthetic "Dev impersonation user" principal authenticated (visible
		// in diagnostic footer) even though the UI claimed signed-out. /clear deletes the
		// cookie outright.
		var view = ReadLayout();
		Assert.Contains("/dev/impersonate/clear", view);
		// And the buggy fallback is gone from the sign-out href branch. The string may
		// still appear in a comment listing prior states, so check the actual href line.
		Assert.DoesNotContain(": \"/dev/impersonate/user\"", view);
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
		Assert.Contains("impersonating CMS admin", view);
		// The label assembles parts with ", " — pins the join separator so the combined
		// "Sign out (Lance Keay, impersonating CMS admin)" output stays consistent.
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
		Assert.Contains("As CMS admin", view);
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
		// Tooltip text moved from the old pink pill onto the As CMS admin menu item.
		Assert.Contains("Take on the CMS admin role for this session.", view);
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
