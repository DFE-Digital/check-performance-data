using Castle.Core.Logging;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

public static class TestExtensions
{
    public static void CheckErrorMessage(this ILogger<ZendeskService> logger, string message)
    {
        logger.Received().Log(
        LogLevel.Error,
        Arg.Any<EventId>(),
        Arg.Is<object>(o => o.ToString()!.Contains(message)),
        Arg.Any<Exception>(),
        Arg.Any<Func<object, Exception?, string>>()
   
        );

    }
}
/// <summary>
/// Unit tests for <see cref="ZendeskService"/>.
/// </summary>
public sealed class ZendeskServiceTests
{
    private readonly IZendeskApi _api = Substitute.For<IZendeskApi>();
    private readonly ILogger<ZendeskService> _logger = Substitute.For<ILogger<ZendeskService>>();
    private readonly PollySettings _settings = new();

    // ------------------------------------------------------------------- helpers

    /// <summary>Creates a default test fixture.</summary>
    private ZendeskService CreateSut() => new(_api, Options.Create(new PollySettings()), _logger);

    // =================================================================== GetTicketAsync

    #region GetTicketAsync

    [Fact]
    public async Task GetTicketAsync_ReturnsDto_WhenApiSucceeds()
    {
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test issue" }
        };
        _api.GetTicket(42).Returns(response);

        var result = await CreateSut().GetTicketAsync(42);

        Assert.NotNull(result.Ticket);
        Assert.Equal("Test issue", result.Ticket.Subject);
    }

    [Fact]
    public async Task GetTicketAsync_ReturnsMappedDto_WithAllFields()
    {
        var fields = new List<CustomField>
        {
            new() { Id = 10L, Value = "P2" },
            new() { Id = 20L, Value = "UK" }
        };

        var response = new GetTicketResponse
        {
            Ticket = new()
            {
                Id = 42L,
                Subject = "Login failure",
                Status = "open",
                RequesterId = 100L,
                CustomFields = fields
            }
        };
        _api.GetTicket(42).Returns(response);

        var result = await CreateSut().GetTicketAsync(42);

        Assert.Equal("Login failure", result.Ticket.Subject);
        Assert.Equal("open", result.Ticket.Status);
        Assert.Equal(100L, result.Ticket.RequesterId);
        Assert.Equal(2, result.Ticket.CustomFields.Count);
    }

    [Fact]
    public async Task GetTicketAsync_ThrowsZendeskApiException_OnApiError()
    {
        _api.GetTicket(Arg.Any<long>())
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException("Network down")));

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketAsync(999));

        Assert.Equal("Failed to retrieve ticket 999.", ex.Message);
    }

    [Fact]
    public async Task GetTicketAsync_ReturnsMappedDto_WithNullSubject()
    {
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 42L, Subject = string.Empty }
        };
        _api.GetTicket(42).Returns(response);

        var result = await CreateSut().GetTicketAsync(42);

        Assert.NotNull(result.Ticket);
    }

    [Fact]
    public async Task GetTicketAsync_ReturnsMappedDto_WithNullRequesterId()
    {
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 42L, Subject = "Test" }
        };
        _api.GetTicket(42).Returns(response);

        var result = await CreateSut().GetTicketAsync(42);

        Assert.Equal(default(long), result.Ticket.RequesterId);
    }

    [Fact]
    public async Task GetTicketAsync_ReturnsMappedDto_WithCustomFields()
    {
        var fields = new List<CustomField> { new() { Id = 10L, Value = "Engineering" } };
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 42L, Subject = "Test", CustomFields = fields }
        };
        _api.GetTicket(42).Returns(response);

        var result = await CreateSut().GetTicketAsync(42);

        Assert.Single(result.Ticket.CustomFields);
        Assert.Equal("Engineering", result.Ticket.CustomFields[0].Value);
    }

    [Fact]
    public async Task GetTicketAsync_Retries_OnTransientFailure_ThenSucceeds()
    {
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Retried" }
        };

        _api.GetTicket(42)
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()), Task.FromResult(response));

        var result = await CreateSut().GetTicketAsync(42);

        Assert.Equal("Retried", result.Ticket!.Subject);
    }

    #endregion

    // ===================================================================== GetTicketComments

    #region GetTicketComments

    [Fact]
    public async Task GetTicketComments_ReturnsDto_WhenApiSucceeds()
    {
        var response = new TicketCommentsResponse
        {
            Comments = new List<TicketComment> { new() 
                { 
                    Id = 1, 
                    Body = "Hello", 
                    AuditId = 100L, 
                    AuthorId = 42L,
                    Public = true, 
                    CreatedAt = DateTime.UtcNow
                } 
            },
            Count = 1
        };
        _api.GetTicketComments(42).Returns(response);

        var result = await CreateSut().GetTicketComments(42);

        Assert.NotNull(result.Comments);
        Assert.Single(result.Comments);
        Assert.Equal("Hello", result.Comments[0].Body);
    }

    [Fact]
    public async Task GetTicketComments_ReturnsEmptyList_WhenNoComments()
    {
        var response = new TicketCommentsResponse
        {
            Comments = new List<TicketComment>(),
            Count = 0
        };
        _api.GetTicketComments(42).Returns(response);

        var result = await CreateSut().GetTicketComments(42);

        Assert.Empty(result.Comments);
    }

    [Fact]
    public async Task GetTicketComments_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetTicketComments(Arg.Any<long>())
            .Returns(Task.FromException<TicketCommentsResponse>(new HttpRequestException()));

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketComments(999));

        Assert.Equal("Failed to retrieve comments for ticket 999.", ex.Message);
    }

    [Fact]
    public async Task GetTicketComments_LogsError_WhenNetworkFailure()
    {
        var networkEx = new HttpRequestException();
        _api.GetTicketComments(Arg.Any<long>()).Returns(Task.FromException<TicketCommentsResponse>(networkEx));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketComments(999));
        _logger.CheckErrorMessage("Error retrieving comments for ticket 999");


        _logger.Received().Log(
        LogLevel.Error,
        Arg.Any<EventId>(),
        Arg.Is<object>(o => o.ToString()!.Contains("Error retrieving comments for ticket 999")),
        Arg.Any<Exception>(),
        Arg.Any<Func<object, Exception?, string>>()
   
        );

    }

    [Fact]
    public async Task GetTicketComments_ReturnsComment_WithAllProperties()
    {
        var comment = new TicketComment
        {
            Id = 1,
            Body = "Test body",
            HtmlBody = "<p>HTML</p>",
            PlainBody = "Plain text",
            AuthorId = 42L,
            Public = true,
            AuditId = 100L,
            CreatedAt = DateTime.UtcNow,
        };

        var response = new TicketCommentsResponse
        {
            Comments = new List<TicketComment> { comment },
            Count = 1
        };
        _api.GetTicketComments(42).Returns(response);

        var result = await CreateSut().GetTicketComments(42);

        Assert.Single(result.Comments);
        var mapped = result.Comments[0];
        Assert.Equal("Test body", mapped.Body);
        Assert.Equal("<p>HTML</p>", mapped.HtmlBody);
        Assert.Equal("Plain text", mapped.PlainBody);
        Assert.True(mapped.Public);
    }

    #endregion

    // ================================================================== GetTicketFields

    #region GetTicketFields

    [Fact]
    public async Task GetTicketFields_ReturnsDto_WhenApiSucceeds()
    {
        var response = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData> { new() { Id = 10L, Title = "Priority" } },
            Count = 1
        };
        _api.GetTicketFields().Returns(response);

        var result = await CreateSut().GetTicketFields();

        Assert.NotNull(result.TicketFields);
        Assert.Single(result.TicketFields);
        Assert.Equal("Priority", result.TicketFields[0].Title);
    }

    [Fact]
    public async Task GetTicketFields_ReturnsEmptyList_WhenNoFields()
    {
        var response = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>(),
            Count = 0
        };
        _api.GetTicketFields().Returns(response);

        var result = await CreateSut().GetTicketFields();

        Assert.Empty(result.TicketFields);
    }

    [Fact]
    public async Task GetTicketFields_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetTicketFields()
            .Returns(Task.FromException<TicketFieldsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketFields());
    }

    [Fact]
    public async Task GetTicketFields_ReturnsCustomField_WithAllProperties()
    {
        var response = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>
            {
                new()
                {
                    Id = 10L,
                    Title = "Region",
                    Description = "User region",
                    Active = true,
                    Position = 1,
                    Key = "region"
                }
            },
            Count = 1
        };
        _api.GetTicketFields().Returns(response);

        var result = await CreateSut().GetTicketFields();

        Assert.Single(result.TicketFields);
        var field = result.TicketFields[0];
        Assert.Equal("Region", field.Title);
        Assert.Equal("User region", field.Description);
        Assert.True(field.Active);
        Assert.Equal(1, field.Position);
    }

    #endregion

    // =============================================================== GetTicketsForView

    #region GetTicketsForView

    [Fact]
    public async Task GetTicketsForView_ReturnsDto_WithQueryParameters()
    {
        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket> { new() { Id = 1L, Subject = "Q1" } },
            Count = 1
        };
        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(response);

        var result = await CreateSut().GetTicketsForView(77);

        Assert.NotNull(result.Tickets);
        Assert.Single(result.Tickets);
    }

    [Fact]
    public async Task GetTicketsForView_ReturnsPagingInfo()
    {
        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            Count = 10,
            NextPage = new Uri("http://example.com/page2"),
            PreviousPage = null
        };
        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(response);

        var result = await CreateSut().GetTicketsForView(77);

        Assert.Equal(10, result.Count);
        Assert.NotNull(result.NextPage);
    }

    [Fact]
    public async Task GetTicketsForView_PassesQueryParameters_Through()
    {
        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            Count = 0
        };

        _api.GetTicketsForView(77, Arg.Is<Dictionary<string, object>>(q => q.ContainsKey("sort_by")))
            .Returns(response);

        await CreateSut().GetTicketsForView(77, new Dictionary<string, object> { ["sort_by"] = "created" });

        await _api.Received(1).GetTicketsForView(77, Arg.Is<Dictionary<string, object>>(q => q.ContainsKey("sort_by")));
    }

    [Fact]
    public async Task GetTicketsForView_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetTicketsForView(Arg.Any<long>(), Arg.Any<Dictionary<string, object>>())
            .Returns(Task.FromException<ListViewTicketsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketsForView(999));
    }

    [Fact]
    public async Task GetTicketsForView_HandlesEmptyTicketsList()
    {
        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            Count = 0
        };
        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(response);

        var result = await CreateSut().GetTicketsForView(77);

        Assert.Empty(result.Tickets);
    }

    [Fact]
    public async Task GetTicketsForView_ReturnsTicket_WithCustomFields()
    {
        var ticket = new Ticket
        {
            Id = 1L,
            Subject = "Test",
            Status = "open",
            CustomFields = new List<CustomField> { new() { Id = 50L, Value = "Finance" } }
        };

        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket> { ticket },
            Count = 1
        };
        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(response);

        var result = await CreateSut().GetTicketsForView(77);

        Assert.Single(result.Tickets);
        var t = result.Tickets[0];
        Assert.Equal("open", t.Status);
        Assert.Single(t.CustomFields);
    }

    #endregion

    // ============================================================= GetTicketsViewModelAsync

    #region GetTicketsViewModelAsync

    [Fact]
    public async Task GetTicketsViewModelAsync_CombinesTicketsAndFields()
    {
        var ticketsResponse = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket> { new() { Id = 1L, Subject = "Test" } },
            Count = 500
        };

        var fieldsResponse = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData> { new() { Id = 10L, Title = "Region" } },
            Count = 1
        };

        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(ticketsResponse);
        _api.GetTicketFields().Returns(fieldsResponse);

        var result = await CreateSut().GetTicketsViewModelAsync(77);

        Assert.NotNull(result.TicketsResponse);
        Assert.Single(result.TicketsResponse!.Tickets);
        Assert.Equal("Region", result.TicketFieldsResponse.TicketFields[0].Title);
    }

    [Fact]
    public async Task GetTicketsViewModelAsync_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetTicketsForView(Arg.Any<long>(), Arg.Any<Dictionary<string, object>>())
            .Returns(Task.FromException<ListViewTicketsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketsViewModelAsync(77));
    }

    [Fact]
    public async Task GetTicketsViewModelAsync_PassesQueryParameters_Through()
    {
        var ticketsResponse = new ListViewTicketsResponse { Tickets = new List<Ticket>(), Count = 0 };
        var fieldsResponse = new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 };

        _api.GetTicketsForView(77, Arg.Is<Dictionary<string, object>>(q => q.ContainsKey("sort_by")))
            .Returns(ticketsResponse);
        _api.GetTicketFields().Returns(fieldsResponse);

        await CreateSut().GetTicketsViewModelAsync(77, new Dictionary<string, object> { ["sort_by"] = "created" });

        await _api.Received(1).GetTicketsForView(
            77, Arg.Is<Dictionary<string, object>>(q => q["sort_by"].ToString() == "created"));
    }

    [Fact]
    public async Task GetTicketsViewModelAsync_HandlesEmptyResponse()
    {
        _api.GetTicketsForView(Arg.Any<long>(), Arg.Any<Dictionary<string, object>>())
            .Returns(new ListViewTicketsResponse { Tickets = new List<Ticket>(), Count = 0 });
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });

        var result = await CreateSut().GetTicketsViewModelAsync(77);

        Assert.Empty(result.TicketsResponse!.Tickets);
    }

    [Fact]
    public async Task GetTicketsViewModelAsync_CombinesTicketsWithMultipleFields()
    {
        var ticketsResponse = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket> { new() { Id = 1L, Subject = "T" } },
            Count = 1
        };

        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>()).Returns(ticketsResponse);
        _api.GetTicketFields().Returns(new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>
            {
                new() { Id = 1L, Title = "Region" },
                new() { Id = 2L, Title = "Priority" }
            },
            Count = 2
        });

        var result = await CreateSut().GetTicketsViewModelAsync(77);

        Assert.Equal(2, result.TicketFieldsResponse.TicketFields.Count);
    }

    #endregion

    // ================================================================ GetTicketViewModelAsync

    #region GetTicketViewModelAsync

    [Fact]
    public async Task GetTicketViewModelAsync_CombinesAllResponses()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test", CustomFields = new List<CustomField>() }
        };

        var fieldsResponse = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData> { new() { Id = 10L, Title = "Region" } },
            Count = 1
        };

        var commentsResponse = new TicketCommentsResponse
        {
            Comments = new List<TicketComment>(),
            Count = 0
        };

        _api.GetTicket(42).Returns(ticketResponse);
        _api.GetTicketFields().Returns(fieldsResponse);
        _api.GetTicketComments(42).Returns(commentsResponse);

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.NotNull(result.Ticket);
        Assert.NotNull(result.UserFields);
        Assert.Single(result.UserFields);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_MapsCustomFieldValues_UsingFields()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new()
            {
                Id = 1L,
                Subject = "Test",
                CustomFields = new List<CustomField>
                {
                    new() { Id = 50L, Value = "Finance" }
                }
            }
        };

        var fieldsResponse = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>
            {
                //new() { Id = 10L, Title = "Region" },
                new() { Id = 50L, Title = "Department" }
            },
            Count = 2
        };

        _api.GetTicket(42).Returns(ticketResponse);
        _api.GetTicketFields().Returns(fieldsResponse);
        _api.GetTicketComments(42).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.Single(result.UserFields);
        Assert.Equal("Finance", result.Ticket.CustomFields[0].Value);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetTicket(Arg.Any<long>())
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketViewModelAsync(42));
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ReturnsComments_WhenAvailable()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new() 
            { 
                Id = 1L, 
                Subject = "Test", 
                CustomFields = new List<CustomField>( ) 
            }
        };

        _api.GetTicket(42).Returns(ticketResponse);
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });

        var commentsResponse = new TicketCommentsResponse
        {
            Comments = new List<TicketComment>
            {
                new() { Id = 1, Body = "Reply from support", AuthorId = 42L, Public = true, AuditId = 1L, CreatedAt = DateTime.UtcNow }
            },
            Count = 1
        };
        _api.GetTicketComments(42).Returns(commentsResponse);

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.Single(result.Comments);
        Assert.Equal("Reply from support", result.Comments[0].Body);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ReturnsEmptyComments_WhenNone()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test", CustomFields = new List<CustomField>() }
        };

        _api.GetTicket(42).Returns(ticketResponse);
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });
        _api.GetTicketComments(42).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.Empty(result.Comments);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_MapsCustomField_UsingIdLookup()
    {
        _api.GetTicket(42).Returns(new GetTicketResponse
        {
            Ticket = new()
            {
                Id = 1L,
                Subject = "Test",
                CustomFields = new List<CustomField> { new() { Id = 10L, Value = "London" } }
            }
        });

        _api.GetTicketFields().Returns(new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>
            {
                new() { Id = 10L, Title = "City" }
            },
            Count = 1
        });

        _api.GetTicketComments(42).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.Single(result.Ticket!.CustomFields);
        Assert.Equal("London", result.Ticket.CustomFields[0].Value);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ReturnsTicket_WithAllProperties()
    {
        var response = new GetTicketResponse
        {
            Ticket = new()
            {
                Id = 1L,
                Subject = "Test subject",
                Status = "open",
                Type = "question",
                Priority = "high",
                RequesterId = 42L,
                AssigneeId = 99L,
                GroupId = 3L,
                Description = "Some description"
            }
        };

        _api.GetTicket(42).Returns(response);
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });
        _api.GetTicketComments(42).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        var result = await CreateSut().GetTicketViewModelAsync(42);

        Assert.NotNull(result.Ticket);
        Assert.Equal("open", result.Ticket.Status);
        Assert.Equal("question", result.Ticket.Type);
        Assert.Equal("high", result.Ticket.Priority);
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ThrowsWhenTicketFails()
    {
        _api.GetTicket(Arg.Any<long>())
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketViewModelAsync(42));
    }

    [Fact]
    public async Task GetTicketViewModelAsync_ThrowsWhenCommentsFails()
    {
        _api.GetTicket(Arg.Any<long>()).Returns(new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test" }
        });

        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });

        _api.GetTicketComments(Arg.Any<long>())
            .Returns(Task.FromException<TicketCommentsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketViewModelAsync(42));
    }

    #endregion

    // ================================================================= GetUserFieldsAsync

    #region GetUserFieldsAsync

    [Fact]
    public async Task GetUserFieldsAsync_ReturnsDto_WhenApiSucceeds()
    {
        var response = new UserFieldsResponse
        {
            UserFields = new List<CustomFieldMetaData> { new() { Id = 10L, Title = "Department" } },
            Count = 1
        };
        _api.GetUserFields().Returns(response);

        var result = await CreateSut().GetUserFieldsAsync();

        Assert.NotNull(result.UserFields);
        Assert.Single(result.UserFields);
    }

    [Fact]
    public async Task GetUserFieldsAsync_ReturnsEmptyList_WhenNoUserFields()
    {
        var response = new UserFieldsResponse
        {
            UserFields = new List<CustomFieldMetaData>(),
            Count = 0
        };
        _api.GetUserFields().Returns(response);

        var result = await CreateSut().GetUserFieldsAsync();

        Assert.Empty(result.UserFields);
    }

    [Fact]
    public async Task GetUserFieldsAsync_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetUserFields()
            .Returns(Task.FromException<UserFieldsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetUserFieldsAsync());
    }

    [Fact]
    public async Task GetUserFieldsAsync_ReturnsCustomField_WithProperties()
    {
        var response = new UserFieldsResponse
        {
            UserFields = new List<CustomFieldMetaData>
            {
                new() { Id = 10L, Title = "Employee ID", Key = "employee_id" }
            },
            Count = 1
        };
        _api.GetUserFields().Returns(response);

        var result = await CreateSut().GetUserFieldsAsync();

        Assert.Single(result.UserFields);
        Assert.Equal("Employee ID", result.UserFields[0].Title);
    }

    #endregion

    // ==================================================================== CreateTicketAsync

    #region CreateTicketAsync

    [Fact]
    public async Task CreateTicketAsync_CreatesMappedRequest()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test subject",
                Description = "Body text"
            }
        };

        var response = new CreateTicketResponse
        {
            Ticket = new() { Id = 99L, Subject = "Test subject" }
        };
        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(response);

        var result = await CreateSut().CreateTicketAsync(request);

        Assert.NotNull(result.Ticket);
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedSubject_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Created subject",
                Description = "Body"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.Subject == "Created subject"));
    }

    // [Fact]
    // public async Task GetTicketComments_LogsError_WhenNetworkFailure()
    // {
    //     var networkEx = new HttpRequestException();
    //     _api.GetTicketComments(Arg.Any<long>()).Returns(Task.FromException<TicketCommentsResponse>(networkEx));

    //     await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().GetTicketComments(999));
    //     _logger.CheckErrorMessage("Error retrieving comments for ticket 999");
    // }

    [Fact]
    public async Task CreateTicketAsync_ReturnsMappedDto_WithCreatedTicket()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "New issue",
                Description = "Something is broken"
            }
        };

        var response = new CreateTicketResponse
        {
            Ticket = new()
            {
                Id = 123L,
                Subject = "New issue",
                Status = "pending",
                RequesterId = 456L
            }
        };
        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(response);

        var result = await CreateSut().CreateTicketAsync(request);

        Assert.Equal(123L, result.Ticket.Id);
        Assert.Equal("pending", result.Ticket.Status);
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedPriority_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body",
                Priority = "high"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.Priority == "high"));
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedCustomFields_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body",
                CustomFields = new List<CustomFieldDto> { new() { Id = 10L, Value = "Engineering" } }
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.CustomFields!.Count == 1));
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedGroupId_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body",
                GroupId = 42L,
                Priority = "high"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.GroupId == 42));
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedType_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body",
                Type = "task"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.Type == "task"));
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedBrandId_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body",
                BrandId = 5L,
                Priority = "high"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.BrandId == 5));
    }

    [Fact]
    public async Task CreateTicketAsync_PassesMappedComment_ToApi()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = "Test",
                Description = "Body with comment"
            }
        };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>()).Returns(
            new CreateTicketResponse { Ticket = new() { Id = 99L, Subject = "Test" } });

        await CreateSut().CreateTicketAsync(request);

        await _api.Received(1).CreateTicket(
            Arg.Is<CreateTicketRequest>(r => r.Ticket!.Description == "Body with comment"));
    }

    #endregion

    // =================================================================== ListViewsAsync

    #region ListViewsAsync

    [Fact]
    public async Task ListViewsAsync_UsesDefaultPageSize()
    {
        var response = new ListViewsResponse();
        _api.GetViews(Arg.Any<int>()).Returns(response);

        await CreateSut().ListViewsAsync(null);

        await _api.Received(1).GetViews(Arg.Is<int>(pageSize => pageSize == 20));
    }

    [Fact]
    public async Task ListViewsAsync_UsesProvidedPageSize()
    {
        var response = new ListViewsResponse();
        _api.GetViews(Arg.Any<int>()).Returns(response);

        await CreateSut().ListViewsAsync(50);

        await _api.Received(1).GetViews(Arg.Is<int>(pageSize => pageSize == 50));
    }

    [Fact]
    public async Task ListViewsAsync_ThrowsZendeskApiException_OnFailure()
    {
        _api.GetViews(Arg.Any<int>())
            .Returns(Task.FromException<ListViewsResponse>(new HttpRequestException()));

        await Assert.ThrowsAnyAsync<ZendeskApiException>(() => CreateSut().ListViewsAsync(null));
    }

    [Fact]
    public async Task ListViewsAsync_ReturnsDto_WhenApiSucceeds()
    {
        var view = new View
        {
            Id = 1L,
            Title = "Open tickets",
            Active = true,
            Position = 1
        };

        var response = new ListViewsResponse
        {
            Views = new List<View> { view }
        };
        _api.GetViews(20).Returns(response);

        var result = await CreateSut().ListViewsAsync(20);

        Assert.NotNull(result.Views);
        Assert.Single(result.Views);
    }

    [Fact]
    public async Task ListViewsAsync_ReturnsEmptyViews_WhenApiReturnsEmpty()
    {
        _api.GetViews(Arg.Any<int>()).Returns(new ListViewsResponse());

        var result = await CreateSut().ListViewsAsync(20);

        Assert.Empty(result.Views);
    }

    [Fact]
    public async Task ListViewsAsync_ReturnsView_WithProperties()
    {
        _api.GetViews(Arg.Any<int>()).Returns(new ListViewsResponse
        {
            Views = new List<View>
            {
                new View { Id = 1L, Title = "Open tickets", Active = true, Position = 5 }
            }
        });

        var result = await CreateSut().ListViewsAsync(20);

        Assert.Single(result.Views);
        Assert.Equal("Open tickets", result.Views[0].Title);
        Assert.True(result.Views[0].Active);
    }

    #endregion

    // =============================================================== Retry behavior (Polly)

    #region RetryBehavior

    [Fact]
    public async Task GetTicketComments_Retries_OnTransientFailure()
    {
        var response = new TicketCommentsResponse
        {
            Comments = new List<TicketComment>(),
            Count = 0
        };

        _api.GetTicketComments(42)
            .Returns(Task.FromException<TicketCommentsResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().GetTicketComments(42);
    }

    [Fact]
    public async Task GetTicketsForView_Retries_OnTransientFailure()
    {
        var response = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            
            Count = 0
        };

        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>())
            .Returns(Task.FromException<ListViewTicketsResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().GetTicketsForView(77);
    }

    [Fact]
    public async Task GetTicketFields_Retries_OnTransientFailure()
    {
        var response = new TicketFieldsResponse
        {
            TicketFields = new List<CustomFieldMetaData>(),
            Count = 0
        };

        _api.GetTicketFields()
            .Returns(Task.FromException<TicketFieldsResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().GetTicketFields();
    }

    [Fact]
    public async Task GetUserFields_Retries_OnTransientFailure()
    {
        var response = new UserFieldsResponse
        {
            UserFields = new List<CustomFieldMetaData>(),
            Count = 0
        };

        _api.GetUserFields()
            .Returns(Task.FromException<UserFieldsResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().GetUserFieldsAsync();
    }

    [Fact]
    public async Task CreateTicket_Retries_OnTransientFailure()
    {
        var request = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto { Subject = "Test", Description = "Body" }
        };
        var response = new CreateTicketResponse { Ticket = new() { Id = 99L } };

        _api.CreateTicket(Arg.Any<CreateTicketRequest>())
            .Returns(Task.FromException<CreateTicketResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().CreateTicketAsync(request);
    }

    [Fact]
    public async Task ListViews_Retries_OnTransientFailure()
    {
        var response = new ListViewsResponse();

        _api.GetViews(Arg.Any<int>())
            .Returns(Task.FromException<ListViewsResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().ListViewsAsync(null);
    }

    [Fact]
    public async Task GetTicketViewModel_Retries_OnTransientFailure()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test" }
        };

        _api.GetTicket(Arg.Any<long>())
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()), Task.FromResult(ticketResponse));
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });
        _api.GetTicketComments(Arg.Any<long>()).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        await CreateSut().GetTicketViewModelAsync(42);
    }

    [Fact]
    public async Task GetTicketsViewModel_Retries_OnTransientFailure()
    {
        var ticketsResponse = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            Count = 0
        };

        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>())
            .Returns(Task.FromException<ListViewTicketsResponse>(new HttpRequestException()), Task.FromResult(ticketsResponse));
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });

        await CreateSut().GetTicketsViewModelAsync(77);
    }

    #endregion

    // =============================================================== Logging verification

    #region LoggingVerification

    [Fact]
    public async Task GetTicketAsync_LogsWarning_OnRetry()
    {
        var response = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "OK" }
        };

        _api.GetTicket(42)
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()), Task.FromResult(response));

        await CreateSut().GetTicketAsync(42);

        // Verify a warning log was produced during retry.
    }

    [Fact]
    public async Task GetTicketViewModelAsync_LogsWarning_OnRetry()
    {
        var ticketResponse = new GetTicketResponse
        {
            Ticket = new() { Id = 1L, Subject = "Test", CustomFields = new List<CustomField>() }
        };

        _api.GetTicket(42)
            .Returns(Task.FromException<GetTicketResponse>(new HttpRequestException()), Task.FromResult(ticketResponse));
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });
        _api.GetTicketComments(42).Returns(new TicketCommentsResponse { Comments = new List<TicketComment>(), Count = 0 });

        await CreateSut().GetTicketViewModelAsync(42);
    }

    [Fact]
    public async Task GetTicketsViewModelAsync_LogsWarning_OnRetry()
    {
        var ticketsResponse = new ListViewTicketsResponse
        {
            Tickets = new List<Ticket>(),
            Count = 0
        };

        _api.GetTicketsForView(77, Arg.Any<Dictionary<string, object>>())
            .Returns(Task.FromException<ListViewTicketsResponse>(new HttpRequestException()), Task.FromResult(ticketsResponse));
        _api.GetTicketFields().Returns(new TicketFieldsResponse { TicketFields = new List<CustomFieldMetaData>(), Count = 0 });

        await CreateSut().GetTicketsViewModelAsync(77);
    }

    #endregion
}