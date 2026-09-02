using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// CheckYourPupilDataViewModel is both the render model and the model-binding target for the
// NextStep POST. The binder creates it through its parameterless constructor and sets only what the
// form posted — `required` is a compile-time rule and buys nothing here — and MVC's validation
// visitor then reads every property on the result. A computed property that dereferences an unset
// one therefore throws before the action runs, which is exactly what Title did.
public sealed class CheckYourPupilDataViewModelBindingTests
{
    private static CheckYourPupilDataViewModel AsTheBinderCreatesIt() =>
        (CheckYourPupilDataViewModel)Activator.CreateInstance(typeof(CheckYourPupilDataViewModel))!;

    [Fact]
    public void Every_readable_property_survives_a_binder_created_instance()
    {
        // The validation visitor reads them all, so any one of them throwing takes the request down.
        var vm = AsTheBinderCreatesIt();

        foreach (var property in typeof(CheckYourPupilDataViewModel).GetProperties()
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
        {
            var exception = Record.Exception(() => property.GetValue(vm));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void The_noun_defaults_to_pupil_rather_than_null()
    {
        var vm = AsTheBinderCreatesIt();

        Assert.Equal("pupil", vm.LearnerNoun.Singular);
        Assert.Equal("Check your pupil data", vm.Title);
    }
}
