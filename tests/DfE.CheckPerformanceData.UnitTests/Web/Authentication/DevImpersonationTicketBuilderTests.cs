using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

public sealed class DevImpersonationTicketBuilderTests
{
	// --- editor value yields a principal with the editor role ---

	[Fact]
	public void TryBuild_EditorValue_ReturnsTicketWithEditorRole()
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.EditorValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.Identity?.IsAuthenticated);
		Assert.True(ticket.Principal.IsInRole(WikiConstants.EditorRole));
	}

	// --- user value yields an authenticated principal without the editor role ---

	[Fact]
	public void TryBuild_UserValue_ReturnsAuthenticatedTicketWithoutEditorRole()
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.UserValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.Identity?.IsAuthenticated);
		Assert.False(ticket.Principal.IsInRole(WikiConstants.EditorRole));
	}

	// --- unknown values produce no ticket so the scheme returns NoResult ---

	[Theory]
	[InlineData("")]
	[InlineData("editor;DROP TABLE")]
	[InlineData("EDITOR")]   // case-sensitive on purpose; the controller writes lowercase
	public void TryBuild_UnknownValue_ReturnsNull(string cookieValue)
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(cookieValue);

		Assert.Null(ticket);
	}

	// --- principal carries an authentication-scheme-tagged identity ---

	[Fact]
	public void TryBuild_TicketIdentityCarriesSchemeAsAuthenticationType()
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.EditorValue);

		Assert.NotNull(ticket);
		var identity = Assert.IsType<ClaimsIdentity>(ticket!.Principal.Identity);
		Assert.Equal(DevImpersonationConstants.Scheme, identity.AuthenticationType);
	}

	// --- the independent value exists so journey conditions can be driven in a browser ---
	//
	// organisation_type_id "11" is what SchoolIsIndependentCondition / SchoolIsNotIndependentCondition
	// read, via CurrentUserService. Without a cookie value that produces it, the two independent-
	// related removal reasons (offered to opposite polarities) cannot be exercised end-to-end at
	// all: every browser session is a type "1" school. Dev-only — the scheme itself is registered
	// solely under !IsProduction() && Dev:ToolsEnabled.

	[Fact]
	public void TryBuild_IndependentValue_StampsIndependentOrganisationType()
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.IndependentUserValue);

		Assert.NotNull(ticket);
		Assert.Equal("11", ticket!.Principal.FindFirst("organisation_type_id")?.Value);
	}

	[Fact]
	public void TryBuild_IndependentValue_CarriesEditorRole()
	{
		// Same privilege as the editor value — no new capability, only a different organisation type.
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.IndependentUserValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.IsInRole(WikiConstants.EditorRole));
	}

	[Fact]
	public void TryBuild_IndependentValue_KeepsTheSeededSchoolClaims()
	{
		// The pupil fixtures are Kingsmead's, so only the type may differ — the journey must
		// still resolve the same pupil data.
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.IndependentUserValue);

		Assert.NotNull(ticket);
		Assert.Equal("142313", ticket!.Principal.FindFirst("organisation_urn")?.Value);
		Assert.Equal("860/4070", ticket.Principal.FindFirst("organisation_laestab")?.Value);
	}

	[Fact]
	public void TryBuild_ExistingValues_KeepTheNonIndependentOrganisationType()
	{
		// Regression: only the new value changes type. The default E2E user stays a type "1"
		// school, so the seeded journey tests are unaffected.
		foreach (var value in new[]
		{
			DevImpersonationConstants.EditorValue,
			DevImpersonationConstants.UserValue,
			DevImpersonationConstants.AdminValue
		})
		{
			var ticket = DevImpersonationTicketBuilder.TryBuild(value);

			Assert.NotNull(ticket);
			Assert.Equal("1", ticket!.Principal.FindFirst("organisation_type_id")?.Value);
		}
	}
}
