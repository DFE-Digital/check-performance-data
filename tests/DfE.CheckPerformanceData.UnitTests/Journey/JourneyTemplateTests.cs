using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyTemplateTests
{
    [Fact]
    public void Strip_replaces_pupilName_token_with_the_pupil()
    {
        var result = JourneyTemplate.Strip("Why are you asking to remove {pupilName} from the performance data?");

        Assert.Equal("Why are you asking to remove the pupil from the performance data?", result);
    }

    [Fact]
    public void Strip_replaces_possessive_pupilName_token_with_the_pupils()
    {
        var result = JourneyTemplate.Strip("What is {pupilName}'s first language?");

        Assert.Equal("What is the pupil's first language?", result);
    }

    [Fact]
    public void Strip_replaces_every_occurrence()
    {
        var result = JourneyTemplate.Strip("Was {pupilName} on roll when {pupilName} left?");

        Assert.Equal("Was the pupil on roll when the pupil left?", result);
    }

    [Fact]
    public void Strip_leaves_text_without_the_token_unchanged()
    {
        var result = JourneyTemplate.Strip("Which pupil do you want to remove?");

        Assert.Equal("Which pupil do you want to remove?", result);
    }
}
