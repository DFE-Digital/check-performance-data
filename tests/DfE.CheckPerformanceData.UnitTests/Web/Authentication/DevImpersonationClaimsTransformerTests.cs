using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

public sealed class DevImpersonationClaimsTransformerTests
{
	private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

	private DevImpersonationClaimsTransformer CreateSut() => new(_httpContextAccessor);

	private void SetCookie(string? value)
	{
		var context = new DefaultHttpContext();
		if (value is not null)
		{
			context.Request.Headers["Cookie"] =
				$"{DevImpersonationConstants.CookieName}={value}";
		}
		_httpContextAccessor.HttpContext.Returns(context);
	}

	private static ClaimsPrincipal PrincipalWithoutEditor() =>
		new(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "user-1")],
			authenticationType: "TestScheme"));

	private static ClaimsPrincipal PrincipalWithEditor() =>
		new(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "user-1"),
				new Claim(ClaimTypes.Role, WikiConstants.EditorRole)
			],
			authenticationType: "TestScheme"));

	// --- CookieAbsent_LeavesPrincipalUnchanged ---

	[Fact]
	public async Task CookieAbsent_LeavesPrincipalUnchanged()
	{
		SetCookie(value: null);
		var principal = PrincipalWithoutEditor();

		var result = await CreateSut().TransformAsync(principal);

		Assert.False(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- CookieValueEditor_AddsEditorRole ---

	[Fact]
	public async Task CookieValueEditor_AddsEditorRole_ToUnprivilegedPrincipal()
	{
		SetCookie(DevImpersonationConstants.EditorValue);
		var principal = PrincipalWithoutEditor();

		var result = await CreateSut().TransformAsync(principal);

		Assert.True(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- CookieValueEditor_DoesNotDuplicateRole ---

	[Fact]
	public async Task CookieValueEditor_DoesNotDuplicateRole_WhenAlreadyPresent()
	{
		SetCookie(DevImpersonationConstants.EditorValue);
		var principal = PrincipalWithEditor();

		var result = await CreateSut().TransformAsync(principal);

		var editorRoleCount = result.Claims
			.Count(c => c.Type == ClaimTypes.Role && c.Value == WikiConstants.EditorRole);
		Assert.Equal(1, editorRoleCount);
	}

	// --- CookieValueUser_RemovesEditorRole ---

	[Fact]
	public async Task CookieValueUser_RemovesEditorRole_FromPrivilegedPrincipal()
	{
		SetCookie(DevImpersonationConstants.UserValue);
		var principal = PrincipalWithEditor();

		var result = await CreateSut().TransformAsync(principal);

		Assert.False(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- CookieValueUnknown_LeavesPrincipalUnchanged ---

	[Fact]
	public async Task CookieValueUnknown_LeavesPrincipalUnchanged()
	{
		SetCookie("anything-else");
		var principal = PrincipalWithEditor();

		var result = await CreateSut().TransformAsync(principal);

		Assert.True(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- CookieValueUser_RemovesEditorRole_WhenRoleIsOnSecondIdentity ---

	[Fact]
	public async Task CookieValueUser_RemovesEditorRole_WhenRoleIsOnSecondIdentity()
	{
		// Matches the real DfE Sign-In shape: OIDC produces the primary identity, then
		// ClaimsEnrichmentService.EnrichAsync adds the role on a SECOND identity via
		// AddIdentity. The transformer must find and strip the role from whichever
		// identity carries it.
		SetCookie(DevImpersonationConstants.UserValue);
		var primary = new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "user-1")],
			authenticationType: "TestScheme");
		var enrichment = new ClaimsIdentity(
			[new Claim(ClaimTypes.Role, WikiConstants.EditorRole)],
			authenticationType: "DfeSignIn");
		var principal = new ClaimsPrincipal(primary);
		principal.AddIdentity(enrichment);

		var result = await CreateSut().TransformAsync(principal);

		Assert.False(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- HttpContextNull_LeavesPrincipalUnchanged ---

	[Fact]
	public async Task HttpContextNull_LeavesPrincipalUnchanged()
	{
		_httpContextAccessor.HttpContext.Returns((HttpContext?)null);
		var principal = PrincipalWithoutEditor();

		var result = await CreateSut().TransformAsync(principal);

		Assert.False(result.IsInRole(WikiConstants.EditorRole));
	}
}
