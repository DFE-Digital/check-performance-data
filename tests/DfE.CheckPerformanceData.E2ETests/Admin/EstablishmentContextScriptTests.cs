using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Some admin actions (move or copy a page in the tree, move a block in the editor) build a form in
// script and call form.submit(). That bypasses the form tag helper and the fetch/XHR wrappers, so
// under a real sign-in the POST arrived without the establishment context and the middleware
// refused it. The local sign-in has no establishment session, so the page is given a stamp here
// and the posts are caught at the network boundary.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class EstablishmentContextScriptTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string Stamp = "e2e-establishment-stamp";

    private async Task OpenPageWithStampAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin");
        await Page.EvaluateAsync(@"stamp => {
            const meta = document.createElement('meta');
            meta.name = 'establishment-context';
            meta.content = stamp;
            document.head.appendChild(meta);
        }", Stamp);
        await Page.AddScriptTagAsync(new() { Url = "/js/establishment-context.js" });
    }

    private async Task<string> PostBodyOfAsync(string script)
    {
        await Page.RouteAsync("**/e2e-context-probe", route =>
            route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "ok" }));
        var request = await Page.RunAndWaitForRequestAsync(() => Page.EvaluateAsync(script), "**/e2e-context-probe");
        return request.PostData ?? string.Empty;
    }

    [Fact]
    public async Task AScriptBuiltFormSubmittedInScript_CarriesTheEstablishmentContext()
    {
        await OpenPageWithStampAsync();

        var body = await PostBodyOfAsync(@"() => {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = '/e2e-context-probe';
            document.body.appendChild(form);
            form.submit();
        }");

        Assert.Contains($"EstablishmentContext={Stamp}", body);
    }

    [Fact]
    public async Task AFormThatAlreadyCarriesTheContext_IsNotGivenASecondOne()
    {
        await OpenPageWithStampAsync();

        var body = await PostBodyOfAsync(@"() => {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = '/e2e-context-probe';
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = 'EstablishmentContext';
            input.value = 'already-there';
            form.appendChild(input);
            document.body.appendChild(form);
            form.submit();
        }");

        Assert.Single(body.Split('&'), pair => pair.StartsWith("EstablishmentContext=", StringComparison.Ordinal));
        Assert.Contains("EstablishmentContext=already-there", body);
    }

    // The site's content security policy stops a post to another site, so the form is checked
    // when it is submitted, and the submit is then cancelled.
    [Fact]
    public async Task AFormPostingToAnotherSite_IsNotGivenTheContext()
    {
        await OpenPageWithStampAsync();

        var fields = await Page.EvaluateAsync<string[]>(@"() => {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = 'https://elsewhere.invalid/probe';
            form.addEventListener('submit', e => e.preventDefault());
            document.body.appendChild(form);
            form.requestSubmit();
            return [...form.elements].map(e => e.name);
        }");

        Assert.DoesNotContain("EstablishmentContext", fields);
    }

    [Fact]
    public async Task AFormSubmittedByItsButton_CarriesTheEstablishmentContext()
    {
        await OpenPageWithStampAsync();

        var body = await PostBodyOfAsync(@"() => {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = '/e2e-context-probe';
            document.body.appendChild(form);
            form.requestSubmit();
        }");

        Assert.Contains($"EstablishmentContext={Stamp}", body);
    }
}
