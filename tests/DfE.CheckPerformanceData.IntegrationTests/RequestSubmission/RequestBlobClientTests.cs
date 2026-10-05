using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

// #536: the document saved at submit is what the close sweep sends to Zendesk, so it must come
// back exactly as it went in, school name included.
[Collection(nameof(AzuriteCollection))]
public sealed class RequestBlobClientTests(AzuriteFixture fixture)
{
    private RequestBlobClient Client() => new(new BlobServiceClient(fixture.ConnectionString));

    [Fact]
    public async Task A_saved_document_reads_back_unchanged()
    {
        var windowId = Guid.NewGuid();
        var document = new RequestDocument
        {
            ChangeRequestId = Guid.NewGuid(),
            ReferenceNumber = "CYPMD_KS4June_RT00001",
            SubmittedBy = new UserDetails { UserId = "u1", DisplayName = "Ada Editor" },
            CheckingWindowId = windowId,
            CheckingWindowType = "KS4June",
            RequestTypeCode = "Add",
            School = new SchoolDetails { Urn = "142313", Name = "Kingsmead School", Laestab = "8604070" },
            Pupil = new PupilDetails
            {
                Id = "p1",
                CypmdId = "c1",
                Firstname = "Alice",
                Surname = "Newpupil",
                DateOfBirth = "2010-01-01",
                Sex = "F",
                Age = 16,
                Upn = "A123456789012"
            },
            Answers = [new AnswerRecord { QuestionId = "q1", QuestionTitle = "Why?", Type = "FreeText", Value = "Joined" }]
        };

        await Client().SaveRequestAsync(windowId, document);
        var read = await Client().GetRequestAsync(windowId, document.ReferenceNumber);

        Assert.NotNull(read);
        Assert.Equal(document.ChangeRequestId, read.ChangeRequestId);
        Assert.Equal("Kingsmead School", read.School.Name);
        Assert.Equal("Add", read.RequestTypeCode);
        Assert.Equal("Joined", Assert.Single(read.Answers).Value);
    }

    [Fact]
    public async Task A_missing_document_reads_as_null()
    {
        Assert.Null(await Client().GetRequestAsync(Guid.NewGuid(), "CYPMD_KS4June_NONE"));
    }
}
