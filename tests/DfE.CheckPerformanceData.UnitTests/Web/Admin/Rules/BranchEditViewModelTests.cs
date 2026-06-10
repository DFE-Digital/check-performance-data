using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class BranchEditViewModelTests
{
    [Fact]
    public void For_Builds_From_Form_With_Dropdown_Data()
    {
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", OutcomeLabel = "EAL", BranchId = "EAL-1",
            Status = DecisionStatus.Scrutiny, LoadETag = "etag-1", IsNew = false,
            Nodes = new List<PredicateNodeForm> { new() { Id = 1, Kind = PredicateKind.AllOf } }
        };

        var vm = BranchEditViewModel.For(form, errors: new[] { "boom" });

        Assert.Same(form, vm.Form);
        Assert.Contains("boom", vm.Errors);
        Assert.Contains("keyStage", vm.AllFields);
        Assert.Contains(DecisionStatus.Scrutiny, vm.Statuses);
    }
}
