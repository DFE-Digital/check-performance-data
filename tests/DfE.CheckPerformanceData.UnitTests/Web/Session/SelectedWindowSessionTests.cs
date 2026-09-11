using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Session;

// The main nav labels its window-scoped link with the selected window's learner noun, so the id
// and the window type have to travel together in the session. These pin that pairing: a 16-19
// window must reach the nav as "Students", a session with no window as "Pupils" (the noun every
// other key stage uses), and clearing must take both keys — a stale type left behind would label
// the next window's link with the last window's noun.
public sealed class SelectedWindowSessionTests
{
    [Theory]
    [InlineData(CheckingWindowType.Post16, "Students")]
    [InlineData(CheckingWindowType.KS4June, "Pupils")]
    [InlineData(CheckingWindowType.KS4Autumn, "Pupils")]
    [InlineData(CheckingWindowType.KS2, "Pupils")]
    public void SetSelectedWindow_StampsTheNounOfTheWindowType(CheckingWindowType type, string expected)
    {
        var session = new FakeSession();
        var windowId = Guid.NewGuid();

        session.SetSelectedWindow(windowId, type);

        Assert.Equal(windowId.ToString(), session.GetSelectedWindowId());
        Assert.Equal(expected, session.GetSelectedWindowLearnerNoun().PluralCapitalised);
    }

    [Fact]
    public void GetSelectedWindowLearnerNoun_NoWindowSelected_FallsBackToPupil()
    {
        var session = new FakeSession();

        Assert.Null(session.GetSelectedWindowId());
        Assert.Equal("Pupils", session.GetSelectedWindowLearnerNoun().PluralCapitalised);
    }

    [Fact]
    public void GetSelectedWindowLearnerNoun_UnreadableType_FallsBackToPupil()
    {
        // A session cookie written before the type was stored alongside the id.
        var session = new FakeSession();
        session.SetString("SelectedWindowId", Guid.NewGuid().ToString());

        Assert.Equal("Pupils", session.GetSelectedWindowLearnerNoun().PluralCapitalised);
    }

    [Fact]
    public void ClearSelectedWindow_RemovesTheTypeAsWellAsTheId()
    {
        var session = new FakeSession();
        session.SetSelectedWindow(Guid.NewGuid(), CheckingWindowType.Post16);

        session.ClearSelectedWindow();

        Assert.Null(session.GetSelectedWindowId());
        Assert.Equal("Pupils", session.GetSelectedWindowLearnerNoun().PluralCapitalised);
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
