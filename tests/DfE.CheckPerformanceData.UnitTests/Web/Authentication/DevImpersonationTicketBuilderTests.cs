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
}
