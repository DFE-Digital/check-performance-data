using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Seeding;

public enum CheckingExercisePocState { ProvisionalOpen, ProvisionalClosed, RevisedClosed, RevisedOpen, Fallback }

public static class CheckingExercisePocSeed
{
    public static readonly Guid WindowId = new("30000000-0000-4000-8000-000000000001");
    public static readonly Guid ProvisionalWindowId = WindowId;
    public static readonly Guid RevisedWindowId = WindowId;
    public const string Laestab = "8604070";

    public static async Task ApplyAsync(PortalDbContext db, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, CheckingExercisePocState state, DateTime now)
    {
        var window = await db.CheckingWindows.Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .SingleOrDefaultAsync(w => w.Id == WindowId);
        if (window is null)
        {
            window = new CheckingWindow
            {
                Id = WindowId,
                Title = "16–19 independent exercises POC",
                KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = now.AddDays(-2),
                EndDate = now.AddDays(30)
            };
            foreach (var stage in new[] { "Provisional", "Revised" })
                foreach (var type in new[] { CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry })
                {
                    var pupil = type == CheckingExerciseType.PupilData;
                    window.CheckingExercises.Add(new CheckingExercise
                    {
                        Id = Guid.NewGuid(),
                        CheckingWindowId = WindowId,
                        ExerciseType = type,
                        Name = $"{stage} {(pupil ? "Students" : "Results")}",
                        TabName = pupil ? "Students" : "Results",
                        TabOrder = pupil ? 0 : 1,
                        StartDate = window.StartDate,
                        EndDate = window.EndDate,
                        Datasets = Enumerable.Range(0, pupil ? 2 : 1).Select(i => new CheckingWindowDataset
                        {
                            Id = Guid.NewGuid(),
                            CheckingWindowId = WindowId,
                            Name = $"input-{i + 1}",
                            SortOrder = i,
                            Included = pupil ? i == 0 : null,
                            SourceFile = pupil ? null : ResultsFileTags.Post16Main
                        }).ToList()
                    });
                }
            foreach (var exercise in window.CheckingExercises.Where(e => e.Name!.StartsWith("Revised ")))
                exercise.ReplacesCheckingExerciseId = window.CheckingExercises
                    .Single(e => e.Name!.StartsWith("Provisional ") && e.ExerciseType == exercise.ExerciseType).Id;
            db.CheckingWindows.Add(window);
            await db.SaveChangesAsync();
        }
        var container = blobs.GetBlobContainerClient(WindowId.ToString());
        await container.CreateIfNotExistsAsync();
        foreach (var exercise in window.CheckingExercises)
        {
            var output = CheckingExerciseBlobPaths.DataBlobName(exercise.Id, CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType), Laestab);
            if (await container.GetBlobClient(output).ExistsAsync()) continue;
            var names = exercise.Name!.StartsWith("Provisional ") ? new[] { "A", "B", "C", "D" } : new[] { "A", "B", "C" };
            foreach (var definition in exercise.Datasets.OrderBy(d => d.SortOrder))
            {
                var pupil = exercise.ExerciseType == CheckingExerciseType.PupilData;
                var rows = pupil ? names.Select((name, i) => (name, i)).Where(x => x.i % 2 == definition.SortOrder)
                    : names.Select((name, i) => (name, i));
                var csv = pupil ? "CYPMD_ID,SURNAME,FORENAMES,LAESTAB,ULN,DOB,SEX\n" : "CYPMD_ID,LAESTAB,QAN,QUAL_NAME,SYLLABUS,SESSION,GRADE\n";
                csv += string.Join("\n", rows.Select(x => pupil
                    ? $"{500001 + x.i},{x.name},Example,{Laestab},{9000000000L + x.i},01/01/2009,F"
                    : $"{500001 + x.i},{Laestab},6014838X,Example qualification,7132,S2026,B")) + "\n";
                var schema = pupil ? """
                    {"type":"object","properties":{
                      "Id":{"type":["string","null"]},"INCLUDED":{"type":"boolean"},
                      "CYPMD_ID":{"type":"string"},"SURNAME":{"type":"string"},"FORENAMES":{"type":"string"},
                      "LAESTAB":{"type":"string"},"ULN":{"type":"string"},"DOB":{"type":"string"},"SEX":{"type":"string"}}}
                    """ : """
                    {"type":"object","properties":{
                      "CYPMD_ID":{"type":"string"},"LAESTAB":{"type":"string"},"QAN":{"type":"string"},
                      "QUAL_NAME":{"type":"string"},"SYLLABUS":{"type":"string"},"SESSION":{"type":"string"},
                      "GRADE":{"type":"string"},"SOURCE":{"type":"string"}}}
                    """;
                definition.IngressFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, definition.Id, "input.csv");
                definition.SchemaFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, definition.Id, "schema.json");
                definition.IngressFileChecksum = Hash(csv);
                definition.SchemaFileChecksum = Hash(schema);
                await container.GetBlobClient(definition.IngressFile).UploadAsync(BinaryData.FromString(csv), overwrite: true);
                await container.GetBlobClient(definition.SchemaFile).UploadAsync(BinaryData.FromString(schema), overwrite: true);
            }
            await db.SaveChangesAsync();
            ValidationProgress? last = null;
            await foreach (var progress in ingress.ProcessAsync(exercise.Id)) last = progress;
            if (last is not { IsComplete: true, IsError: false }) throw new InvalidOperationException("POC ingress failed.");
        }
        var revised = state is CheckingExercisePocState.RevisedClosed or CheckingExercisePocState.RevisedOpen;
        var open = state is CheckingExercisePocState.ProvisionalOpen or CheckingExercisePocState.RevisedOpen;
        db.Entry(window).Property(w => w.StartDate).CurrentValue = now.AddDays(-2);
        db.Entry(window).Property(w => w.EndDate).CurrentValue = open ? now.AddDays(30) : now.AddDays(-1);
        foreach (var exercise in window.CheckingExercises)
        {
            exercise.IsEnabled = (exercise.Name!.StartsWith("Revised ")) == revised;
            exercise.StartDate = window.StartDate;
            exercise.EndDate = window.EndDate;
        }
        await db.SaveChangesAsync();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
