using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public static class SeedQuestionFlows
{
    public static async Task ExecuteSeedAsync(IQuestionFlowBlobClient client, string contentRootPath)
    {
        var dir = Path.Combine(contentRootPath, "Data", "QuestionFlows");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_', 2);
            if (parts.Length != 2) continue;
            if (!Enum.TryParse<WhatToChange>(parts[0], out var whatToChange)) continue;
            if (!Enum.TryParse<CheckingWindowType>(parts[1], out var windowType)) continue;

            var json = await File.ReadAllTextAsync(file);
            await client.UploadConfigAsync(whatToChange, windowType, json);
        }
    }
}
