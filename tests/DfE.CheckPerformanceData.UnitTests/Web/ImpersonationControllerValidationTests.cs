using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationControllerValidationTests
{
    [Fact]
    public async Task Valid_identifiers_are_accepted_even_when_absent_from_the_dropdown_directory()
    {
        var contentRoot = CreateDirectoryFile();
        try
        {
            var directory = new EstablishmentDirectory(Environment(contentRoot));
            var (controller, session, audit) = await CreateController(directory);
            var result = await controller.Start(new ImpersonationViewModel
            {
                Laestab = "9876543", Urn = "999999", LowestAge = 3, HighestAge = 19
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.False(session.IsImpersonating);
            Assert.Equal("9876543", session.PendingSelection?.Laestab);
            Assert.Equal("999999", session.PendingSelection?.Urn);
            Assert.Equal(3, session.PendingSelection?.LowestAge);
            Assert.Equal(19, session.PendingSelection?.HighestAge);
            await audit.DidNotReceive().RecordAsync(Arg.Any<ImpersonationAuditRecord>(), Arg.Any<CancellationToken>());
        }
        finally { Directory.Delete(contentRoot, recursive: true); }
    }

    [Fact]
    public async Task Start_uses_the_submitted_age_range_for_the_selected_establishment()
    {
        var contentRoot = CreateDirectoryFile();
        try
        {
            var directory = new EstablishmentDirectory(Environment(contentRoot));
            var (controller, session, _) = await CreateController(directory);
            var result = await controller.Start(new ImpersonationViewModel
            {
                Laestab = "1230456", Urn = "876543", LowestAge = 100, HighestAge = 101
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.False(session.IsImpersonating);
            Assert.Equal(100, session.PendingSelection?.LowestAge);
            Assert.Equal(101, session.PendingSelection?.HighestAge);
        }
        finally { Directory.Delete(contentRoot, recursive: true); }
    }

    [Fact]
    public void Start_and_Exit_posts_require_anti_forgery_validation()
    {
        var controller = typeof(ImpersonationController);
        foreach (var name in new[] { nameof(ImpersonationController.Start), nameof(ImpersonationController.Begin), nameof(ImpersonationController.Exit) })
        {
            var action = controller.GetMethod(name)!;
            Assert.NotNull(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
            Assert.NotNull(action.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).SingleOrDefault());
        }
    }

    private static string CreateDirectoryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"impersonation-controller-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "Data", "Impersonation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "EstablishmentDetails.csv"),
            "\"URN\",\"LA (code)\",\"EstablishmentNumber\",\"EstablishmentName\",\"StatutoryLowAge\",\"StatutoryHighAge\"\n" +
            "\"876543\",\"123\",\"0456\",\"Example Secondary School\",3,19\n");
        return root;
    }

    private static IWebHostEnvironment Environment(string root)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(root);
        return environment;
    }

    private static async Task<(ImpersonationController Controller, ImpersonationSessionService Session, IImpersonationAuditWriter Audit)> CreateController(EstablishmentDirectory directory)
    {
        var original = Substitute.For<ICurrentUserService>();
        original.UserId.Returns("original-admin");
        original.OrganisationLaestab.Returns("1000001");
        original.OrganisationUrn.Returns("100001");
        var store = Substitute.For<IImpersonationSessionStore>();
        store.GetAsync("test-binding", Arg.Any<CancellationToken>()).Returns(Task.FromResult<ImpersonationSessionState?>(null));
        store.SetAsync(Arg.Any<string>(), Arg.Any<ImpersonationSessionState>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, ImpersonationAccessPolicy.AdminRole)], "Test"))
        };
        http.Session = new TestSession();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var session = new ImpersonationSessionService(accessor, original, store,
            new EphemeralDataProtectionProvider(), Substitute.For<ICheckYourPupilDataRepository>());
        await session.InitialiseAsync("test-binding", CancellationToken.None);
        var audit = Substitute.For<IImpersonationAuditWriter>();
        http.Items[AuthenticatedSessionBinding.ItemKey] = "test-binding";
        var controller = new ImpersonationController(session, audit, original, directory,
            Substitute.For<IWindowRepository>(), Substitute.For<IIngressSummaryStore>(),
            Substitute.For<ILogger<ImpersonationController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return (controller, session, audit);
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[]? value) => values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
