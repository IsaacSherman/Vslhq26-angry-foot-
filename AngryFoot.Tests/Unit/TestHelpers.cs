using AngryFoot.ApiService.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;

namespace AngryFoot.Tests.Unit;

internal static class ChatClientMocks
{
    public static Mock<IChatClient> ReturningText(string text)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return mock;
    }

    public static Mock<IChatClient> Throwing(Exception exception)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        return mock;
    }
}

/// <summary>
/// Owns an open in-memory SQLite connection; the database lives as long as the connection.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AngryFootDbContext> _options;
    private readonly List<AngryFootDbContext> _contexts = [];

    /// <summary>A default shared context for single-context tests.</summary>
    public AngryFootDbContext Context { get; }

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AngryFootDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = CreateContext();
        Context.Database.EnsureCreated();
    }

    /// <summary>
    /// Creates a fresh context over the same database, mirroring production where every
    /// request gets its own scoped DbContext with an empty change tracker.
    /// </summary>
    public AngryFootDbContext CreateContext()
    {
        var context = new AngryFootDbContext(_options);
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _connection.Dispose();
    }
}
