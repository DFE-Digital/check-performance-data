using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;
using DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;
using DfE.CheckPerformanceData.Web.Navigation;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using System.Reflection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Navigation;

// The service navigation marks the section that the user is in (#854). These tests pin how the
// section comes from the endpoint metadata, and how a journey page takes the Amendment requests
// section when the school came from Edit.
public sealed class ServiceNavigationTests
{
    private static readonly Guid WindowId = Guid.NewGuid();

    private static DefaultHttpContext Context(params object[] metadata)
    {
        var context = new DefaultHttpContext { Session = new FakeSession() };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        context.Request.RouteValues["windowId"] = WindowId.ToString();
        return context;
    }

    [Fact]
    public void ActiveSection_NoEndpoint_IsNull()
    {
        var context = new DefaultHttpContext { Session = new FakeSession() };

        Assert.Null(ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_EndpointWithNoAttribute_IsNull()
    {
        Assert.Null(ServiceNavigation.ActiveSection(Context()));
    }

    [Theory]
    [InlineData(ServiceNavSection.Home)]
    [InlineData(ServiceNavSection.Pupils)]
    [InlineData(ServiceNavSection.AmendmentRequests)]
    public void ActiveSection_NavSection_IsThatSection(ServiceNavSection section)
    {
        Assert.Equal(section, ServiceNavigation.ActiveSection(Context(new ServiceNavSectionAttribute(section))));
    }

    [Fact]
    public void ActiveSection_ActionAttributeAfterClassAttribute_UsesTheActionAttribute()
    {
        var context = Context(
            new ServiceNavSectionAttribute(ServiceNavSection.Pupils),
            new ServiceNavSectionAttribute(ServiceNavSection.AmendmentRequests));

        Assert.Equal(ServiceNavSection.AmendmentRequests, ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_Journey_NoEditMode_IsPupils()
    {
        var context = Context(new ServiceNavSectionAttribute(ServiceNavSection.Journey));

        Assert.Equal(ServiceNavSection.Pupils, ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_Journey_SingleEditMode_IsAmendmentRequests()
    {
        var context = Context(new ServiceNavSectionAttribute(ServiceNavSection.Journey));
        context.Session.SetSingleEditMode(WindowId);

        Assert.Equal(ServiceNavSection.AmendmentRequests, ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_Journey_BulkEditMode_IsAmendmentRequests()
    {
        var context = Context(new ServiceNavSectionAttribute(ServiceNavSection.Journey));
        context.Session.SetBulkEditMode(WindowId);

        Assert.Equal(ServiceNavSection.AmendmentRequests, ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_Journey_EditModeForAnotherWindow_IsPupils()
    {
        var context = Context(new ServiceNavSectionAttribute(ServiceNavSection.Journey));
        context.Session.SetSingleEditMode(Guid.NewGuid());

        Assert.Equal(ServiceNavSection.Pupils, ServiceNavigation.ActiveSection(context));
    }

    [Fact]
    public void ActiveSection_Journey_NoWindowId_IsPupils()
    {
        var context = Context(new ServiceNavSectionAttribute(ServiceNavSection.Journey));
        context.Request.RouteValues.Remove("windowId");

        Assert.Equal(ServiceNavSection.Pupils, ServiceNavigation.ActiveSection(context));
    }

    [Theory]
    [InlineData("/CheckYourPupilData/abc", "/CheckYourPupilData/abc", true)]
    [InlineData("/checkyourpupildata/ABC/", "/CheckYourPupilData/abc", true)]
    [InlineData("/Journey/abc/summary", "/CheckYourPupilData/abc", false)]
    [InlineData("/CheckYourPupilData/abc", null, false)]
    public void IsCurrentPage_ComparesThePath(string path, string? href, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.Equal(expected, ServiceNavigation.IsCurrentPage(context.Request, href));
    }

    [Theory]
    [InlineData(typeof(LandingPageController), ServiceNavSection.Home)]
    [InlineData(typeof(DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData.CheckYourPupilDataController), ServiceNavSection.Pupils)]
    [InlineData(typeof(WhatToChangeController), ServiceNavSection.Pupils)]
    [InlineData(typeof(ResultIssueController), ServiceNavSection.Pupils)]
    [InlineData(typeof(ConfirmCorrectController), ServiceNavSection.Pupils)]
    [InlineData(typeof(DfE.CheckPerformanceData.Web.Controllers.Journey.JourneyController), ServiceNavSection.Journey)]
    [InlineData(typeof(AmendmentRequestsController), ServiceNavSection.AmendmentRequests)]
    [InlineData(typeof(SubmittedRequestController), ServiceNavSection.AmendmentRequests)]
    [InlineData(typeof(EstablishmentAmendmentRequestsController), ServiceNavSection.AmendmentRequests)]
    public void SchoolController_HasItsNavSection(Type controller, ServiceNavSection expected)
    {
        var attribute = controller.GetCustomAttribute<ServiceNavSectionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(expected, attribute.Section);
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        public void Set(string key, byte[] value) => _store[key] = value;
        public void Remove(string key) => _store.Remove(key);
        public void Clear() => _store.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
    }
}
