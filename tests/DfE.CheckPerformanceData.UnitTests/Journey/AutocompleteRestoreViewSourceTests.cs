namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Source-text assertions pinning the fix for AB#295434: a selected autocomplete value
// (the country "bounded single value selection list", and the pupil search) had to be
// reselected after a validation-error reload, the in-page back link, browser-back, or
// editing an unsubmitted draft.
//
// Root cause: both views initialised accessible-autocomplete and then wrote the restored
// label straight into the input's DOM value. The component is a Preact-controlled input —
// its internal query state stayed empty, so the component's next re-render (focus, hover)
// rewrote the input from state and wiped the restored label, forcing a reselect; typing
// then popped the suggestion menu over the following question. The supported way to
// restore a value is the component's defaultValue option, which seeds the internal state
// so re-renders preserve it and the menu stays closed.
//
// These tests pin: (a) defaultValue is passed, (b) the post-init DOM poke is gone,
// (c) _Autocomplete's {field}_code hidden input round-trips the stored CodeValue instead
// of being reset to "" on every re-render (which silently dropped the ISO code and left
// OriginCountryLanguageCapture's exact-name recovery doing the real work).
public sealed class AutocompleteRestoreViewSourceTests
{
    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string ViewSource(string viewName) =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", viewName));

    [Fact]
    public void Autocomplete_PassesRestoredLabelAsDefaultValue()
    {
        var view = ViewSource("_Autocomplete.cshtml");

        // The restored label must reach the component as config, not a DOM poke.
        Assert.Contains("defaultValue: initialLabel", view);
    }

    [Fact]
    public void Autocomplete_DoesNotPokeTheInputValueAfterInit()
    {
        var view = ViewSource("_Autocomplete.cshtml");

        // The historic bug: writing the value into the DOM after init left the
        // component's internal state empty, so its next re-render wiped the value.
        Assert.DoesNotContain(".value = initialLabel", view);
    }

    [Fact]
    public void Autocomplete_CodeHiddenField_RoundTripsTheStoredCode()
    {
        var view = ViewSource("_Autocomplete.cshtml");

        // The {field}_code hidden input must re-render with the stored CodeValue.
        // A hard-coded value="" dropped the ISO code on every validation-error
        // resubmit, leaving recovery to an exact-name lookup.
        Assert.Contains("ExistingAnswer?.CodeValue", view);
        Assert.DoesNotContain("id=\"@(Model.FieldName)-code-value\" value=\"\"", view);
    }

    [Fact]
    public void PupilSearch_PassesRestoredLabelAsDefaultValue()
    {
        var view = ViewSource("PupilSearch.cshtml");

        // PupilSearch mirrors _Autocomplete.cshtml (its own comment says so) and had
        // the identical restore defect.
        Assert.Contains("defaultValue: initialLabel", view);
    }

    [Fact]
    public void PupilSearch_DoesNotPokeTheInputValueAfterInit()
    {
        var view = ViewSource("PupilSearch.cshtml");

        Assert.DoesNotContain(".value = initialLabel", view);
    }

    [Fact]
    public void Autocomplete_SuppressesFocusReopenForUneditedRestoredValue()
    {
        var view = ViewSource("_Autocomplete.cshtml");

        // AB#295434 follow-up: accessible-autocomplete hardcodes validChoiceMade: false
        // regardless of defaultValue, so a plain defaultValue restore still reopens the
        // suggestion menu on the first focus. This is suppressed via a document-level
        // capturing focus listener that stops the library's own focus handler from
        // running until the user actually edits the restored value.
        Assert.Contains("document.addEventListener('focus'", view);
        Assert.Contains("stopImmediatePropagation", view);
        Assert.Contains("restoredUnedited", view);
    }

    [Fact]
    public void PupilSearch_SuppressesFocusReopenForUneditedRestoredValue()
    {
        var view = ViewSource("PupilSearch.cshtml");

        Assert.Contains("document.addEventListener('focus'", view);
        Assert.Contains("stopImmediatePropagation", view);
        Assert.Contains("restoredUnedited", view);
    }
}
