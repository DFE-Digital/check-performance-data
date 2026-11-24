using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Media library folders in a browser, as the dev admin: the folder tree in the admin nav, creating
// a folder and moving images into it, "Include subfolders" on and off, and the picker's folder
// tree with an upload into the chosen folder. Each test makes its own folders, named
// e2e-{guid}-..., and deletes them and its images again.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class MediaFolderTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private readonly string _term = $"e2e-{Guid.NewGuid():N}";
    private readonly List<Guid> _images = [];
    private readonly List<Guid> _folders = [];
    private readonly List<Guid> _pages = [];

    public override async Task DisposeAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            for (var i = _pages.Count - 1; i >= 0; i--)
                await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _pages[i]);
            foreach (var id in _images) await MediaSeedHelpers.ClearUsagesAsync(Fixture.SeedClient, id);
            foreach (var id in _images) await MediaSeedHelpers.DeleteAsync(Fixture.SeedClient, id);
            // Inner folders were added last, so they go first.
            for (var i = _folders.Count - 1; i >= 0; i--)
                await MediaFolderHelpers.DeleteAsync(Fixture.SeedClient, _folders[i]);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
            await base.DisposeAsync();
        }
    }

    private async Task SignInAsAdminAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (!string.IsNullOrEmpty(adminCookie) && adminCookie.IndexOf('=') is var eq and > 0)
            await Context.AddCookiesAsync([new Microsoft.Playwright.Cookie { Name = adminCookie[..eq], Value = adminCookie[(eq + 1)..], Url = Fixture.BaseUrl }]);
    }

    private async Task<Guid> FolderAsync(string name, Guid? parent = null)
    {
        var id = await MediaFolderHelpers.CreateAsync(Fixture.SeedClient, name, parent);
        _folders.Add(id);
        return id;
    }

    private async Task<Guid> ImageAsync(Guid? folder, string name)
    {
        var id = await MediaFolderHelpers.UploadIntoAsync(Fixture.SeedClient, folder, $"{_term}-{name}.png", MediaFixtures.UniquePng());
        _images.Add(id);
        return id;
    }

    private ILocator Items => Page.Locator("[data-media-library-region] [data-media-item]");

    [Fact]
    public async Task TheAdminNav_ShowsTheFolderTree_AndAFolderOpensTheLibraryInIt()
    {
        await SignInAsAdminAsync();
        var parent = await FolderAsync($"{_term}-parent");
        var child = await FolderAsync($"{_term}-child", parent);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media");
        var nav = Page.Locator("#admin-rail");
        var childLink = nav.Locator($"a[href='/admin/media?folder={child:D}']");
        var parentLink = nav.Locator($"a[href='/admin/media?folder={parent:D}']");
        await Expect(parentLink).ToHaveTextAsync($"{_term}-parent");

        // A branch opens when its folder is the page shown, as in the page tree.
        await parentLink.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains($"folder={parent:D}"));
        await childLink.ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains($"folder={child:D}"));
        await Expect(Page.Locator("h1")).ToHaveTextAsync($"{_term}-child");
        var crumbs = Page.Locator("[data-media-breadcrumbs]");
        await Expect(crumbs.GetByRole(AriaRole.Link, new() { Name = $"{_term}-parent" })).ToHaveAttributeAsync("href", $"/admin/media?folder={parent:D}");
        await Expect(crumbs.Locator("[aria-current='page']")).ToHaveTextAsync($"{_term}-child");
        await Expect(nav.Locator($"a[href='/admin/media?folder={child:D}']")).ToHaveAttributeAsync("aria-current", "page");
    }

    [Fact]
    public async Task AnEditor_CreatesAFolder_AndMovesTheTickedImagesIntoIt()
    {
        await SignInAsAdminAsync();
        var parent = await FolderAsync($"{_term}-parent");
        var one = await ImageAsync(null, "one");
        var two = await ImageAsync(null, "two");
        await ImageAsync(null, "three");

        // Create a folder inside the parent from the library page.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder={parent:D}");
        await Page.Locator("#new-folder-name").FillAsync($"{_term}-made");
        await Page.Locator("[data-media-new-folder-submit]").ClickAsync();
        await Expect(Page.Locator("[data-media-success]")).ToContainTextAsync($"Folder ‘{_term}-made’ created");
        var made = Page.Locator("[data-media-folders] a", new() { HasText = $"{_term}-made" });
        await Expect(made).ToBeVisibleAsync();
        var madeId = Guid.Parse((await made.GetAttributeAsync("href"))!.Split("folder=")[1][..36]);
        _folders.Add(madeId);

        // A second folder of the same name, in another case, is refused.
        await Page.Locator("#new-folder-name").FillAsync($"{_term}-MADE");
        await Page.Locator("[data-media-new-folder-submit]").ClickAsync();
        await Expect(Page.Locator("[data-media-new-folder-error-link]")).ToContainTextAsync("There is already a folder called");

        // Tick two images at the top of the library and move them into the new folder.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}&sort=name");
        await Expect(Items).ToHaveCountAsync(3);
        await Page.Locator($"#select-{one:D}").CheckAsync();
        await Page.Locator($"#select-{two:D}").CheckAsync();
        await Page.Locator("#move-target").SelectOptionAsync(madeId.ToString());
        await Page.Locator("[data-media-move-submit]").ClickAsync();

        await Expect(Page.Locator("[data-media-success]")).ToContainTextAsync($"2 images moved to ‘{_term}-parent / {_term}-made’");
        Assert.Contains($"search={_term}", Page.Url);
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder={madeId:D}&subfolders=false");
        await Expect(Items).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task IncludeSubfolders_On_ShowsTheWholeBranch_AndOff_ShowsOnlyTheFolder()
    {
        await SignInAsAdminAsync();
        var parent = await FolderAsync($"{_term}-parent");
        var child = await FolderAsync($"{_term}-child", parent);
        var inParent = await ImageAsync(parent, "in-parent");
        var inChild = await ImageAsync(child, "in-child");

        // Ticked by default: the parent and everything inside it.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder={parent:D}");
        var box = Page.GetByLabel("Include subfolders");
        await Expect(box).ToBeCheckedAsync();
        await Expect(Items).ToHaveCountAsync(2);

        // Off: the parent's own images only, and the choice stays in the address.
        await box.UncheckAsync();
        await Page.Locator("[data-media-library-filters] button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("subfolders=false"));
        await Expect(Items).ToHaveCountAsync(1);
        await Expect(Page.Locator($"[data-media-item='{inParent:D}']")).ToBeVisibleAsync();

        // The view keeps the folder and the choice.
        await Page.GetByLabel("View", new() { Exact = true }).SelectOptionAsync("details");
        await Page.WaitForURLAsync(url => url.Contains("view=details"));
        Assert.Contains($"folder={parent:D}", Page.Url);
        Assert.Contains("subfolders=false", Page.Url);
        await Expect(Page.Locator("[data-media-library-table] [data-media-item]")).ToHaveCountAsync(1);

        // Ticked again: the form sends true and the hidden false, and the first value wins.
        await Page.GetByLabel("Include subfolders").CheckAsync();
        await Page.Locator("[data-media-library-filters] button[type='submit']").ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("subfolders=false") || url.Contains("subfolders=true"));
        await Expect(Page.GetByLabel("Include subfolders")).ToBeCheckedAsync();
        await Expect(Page.Locator("[data-media-library-table] [data-media-item]")).ToHaveCountAsync(2);
        await Expect(Page.Locator($"#admin-rail a[href='/admin/media?folder={parent:D}']")).ToHaveAttributeAsync("aria-current", "page");

        // At the top of the library, off means the images in no folder: these two are in folders.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}&subfolders=false");
        await Expect(Page.Locator("[data-media-library-empty]")).ToBeVisibleAsync();
        // The browser remembers Details from above, so the grid is asked for by name.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?search={_term}&view=grid");
        await Expect(Items).ToHaveCountAsync(2);
        await Expect(Page.Locator($"[data-media-item='{inChild:D}'] [data-media-item-folder]")).ToContainTextAsync($"{_term}-parent / {_term}-child");
    }

    [Fact]
    public async Task AFolderThatHoldsThings_CannotBeDeleted_NorMovedInsideItself()
    {
        await SignInAsAdminAsync();
        var parent = await FolderAsync($"{_term}-parent");
        var child = await FolderAsync($"{_term}-child", parent);

        Assert.Equal(HttpStatusCode.Conflict, await MediaFolderHelpers.DeleteAsync(Fixture.SeedClient, parent));
        var (status, body) = await MediaFolderHelpers.MoveAsync(Fixture.SeedClient, parent, child.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("You cannot move a folder into itself or into a folder inside it", body);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media/folders/{parent:D}");
        await Expect(Page.Locator("[data-media-folder-not-empty]")).ToContainTextAsync("because it holds 1 folder");
    }

    [Fact]
    public async Task ThePicker_ChoosesAFolder_WithOrWithoutSubfolders_AndUploadsIntoIt()
    {
        await SignInAsAdminAsync();
        var parent = await FolderAsync($"{_term}-parent");
        var child = await FolderAsync($"{_term}-child", parent);
        var inParent = await ImageAsync(parent, "in-parent");
        var inChild = await ImageAsync(child, "in-child");

        var segment = $"e2e-folder-picker-{Guid.NewGuid():N}";
        var pageId = await CmsSeedHelpers.CreatePageNodeAsync(Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E folder picker");
        _pages.Add(pageId);
        await CmsSeedHelpers.AddWidgetAsync(Fixture.SeedClient, pageId, "0.0", "image");
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit");
        var widget = Page.Locator("[id='widget-0.0']");
        await widget.Locator("summary").First.ClickAsync();
        await Expect(widget.Locator("[data-image-editor-init='true']")).ToBeAttachedAsync();
        await widget.Locator("[data-image-editor-choose]").ClickAsync();
        var dialog = Page.Locator("#media-picker");
        await Expect(dialog).ToBeVisibleAsync();

        // Choose the parent folder: its image and the child folder's image both show.
        var folderButton = dialog.Locator($"[data-media-picker-folder='{parent:D}']");
        await folderButton.ClickAsync();
        await Expect(folderButton).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(dialog.Locator("[data-media-picker-status]")).ToContainTextAsync($"2 images in ‘{_term}-parent’");
        await Expect(dialog.Locator($"#media-picker-item-{inChild:D}")).ToBeAttachedAsync();

        // Untick "subfolders": only the parent's own image.
        await dialog.GetByLabel("Find images in this folder and its subfolders").UncheckAsync();
        await Expect(dialog.Locator("[data-media-picker-status]")).ToContainTextAsync($"1 image in ‘{_term}-parent’");
        await Expect(dialog.Locator($"#media-picker-item-{inParent:D}")).ToBeAttachedAsync();
        await Expect(dialog.Locator($"#media-picker-item-{inChild:D}")).Not.ToBeAttachedAsync();

        // The arrow keys move between the folders; the chosen folder is the one Tab stop.
        await folderButton.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(dialog.Locator($"[data-media-picker-folder='{child:D}']")).ToBeFocusedAsync();
        await Expect(dialog.Locator($"[data-media-picker-folder='{child:D}']")).ToHaveAttributeAsync("tabindex", "-1");

        // Choose the child and upload: the file goes into the child folder.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(dialog.Locator("[data-media-picker-status]")).ToContainTextAsync($"1 image in ‘{_term}-child’");
        var name = $"{_term}-uploaded.png";
        await dialog.Locator("#media-picker-file").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "image/png", Buffer = MediaFixtures.UniquePng() });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Expect(dialog.Locator("[data-media-picker-status]")).ToContainTextAsync($"{name} uploaded and selected");
        var uploaded = Guid.Parse((await dialog.Locator("input[name='media-picker-item']:checked").GetAttributeAsync("value"))!);
        _images.Add(uploaded);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/media?folder={child:D}&subfolders=false");
        await Expect(Page.Locator($"[data-media-item='{uploaded:D}']")).ToBeVisibleAsync();
    }
}
