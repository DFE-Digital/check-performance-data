using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.QuestionFlow;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class WindowAdminControllerTests
{
    [Theory]
    [InlineData(-1, 1, true, "Open", "green")]
    [InlineData(1, 2, true, "Upcoming", "green")]
    [InlineData(-2, -1, true, "Closed", "grey")]
    [InlineData(0, 1, true, "Open", "green")]
    [InlineData(-1, 0, true, "Open", "green")]
    [InlineData(-1, 1, false, "Missing journeys", "red")]
    [InlineData(1, 2, false, "Missing journeys", "red")]
    [InlineData(-2, -1, false, "Missing journeys", "red")]
    public async Task Index_uses_exercise_dates_and_prioritises_missing_flows(
        int start, int end, bool exists, string status, string colour)
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var service = Substitute.For<IWindowService>();
        var flows = Substitute.For<IQuestionFlowConfigSource>();
        flows.Exists(Arg.Any<WhatToChange>(), CheckingWindowType.Post16).Returns(true);
        flows.Exists(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(exists);
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult
        {
            Windows = [new CheckingWindowDto
            {
                Title = "Test window", KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = now.DateTime.AddDays(-10), EndDate = now.DateTime.AddDays(10),
                Exercises = [new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    StartDate = now.DateTime.AddDays(start), EndDate = now.DateTime.AddDays(end)
                }, new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData, SortOrder = -1,
                    StartDate = now.DateTime.AddDays(-1), EndDate = now.DateTime.AddDays(1)
                }]
            }]
        });
        var controller = new WindowAdminController(service, flows, new FixedClock(now));

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var model = Assert.IsType<WindowViewModel>(result.Model);
        var exercises = Assert.Single(model.Windows).Exercises;
        Assert.Equal("Open", exercises[0].Status);
        Assert.Empty(exercises[0].MissingJourneys);
        Assert.Equal(status, exercises[1].Status);
        Assert.Equal(colour, exercises[1].TagColour);
        Assert.Equal(exists ? [] : new[] { "Missing qualification" }, exercises[1].MissingJourneys);
        await flows.DidNotReceive().GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>());
    }

    [Fact]
    public void Exists_checks_the_exact_file_without_parsing_its_contents()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var directory = Path.Combine(root, "Data", "QuestionFlows");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Add_Post16.json"), "not JSON");
            var source = new FileSystemQuestionFlowClient(root);
            Assert.True(source.Exists(WhatToChange.Add, CheckingWindowType.Post16));
            Assert.False(source.Exists(WhatToChange.Add, CheckingWindowType.KS4June));
            Assert.False(source.Exists(WhatToChange.Remove, CheckingWindowType.Post16));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
