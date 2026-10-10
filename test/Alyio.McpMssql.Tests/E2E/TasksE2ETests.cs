// MIT License

using System.Text.Json;
using Alyio.McpMssql.Tests.Infrastructure.Fixtures;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace Alyio.McpMssql.Tests.E2E;

/// <summary>
/// Calls from a client that declares the Tasks extension. The plain
/// CallToolAsync tests elsewhere cover clients that do not.
/// </summary>
public class TasksE2ETests(McpServerFixture fixture) : IClassFixture<McpServerFixture>
{
    private const string Sql = "SELECT TOP 1 name FROM sys.objects";
    private readonly McpClient _client = fixture.Client;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("run_query", "snapshot", true, true)]
    [InlineData("run_query", "snapshot", false, false)]
    [InlineData("analyze_query", "estimated", false, true)]
    [InlineData("analyze_query", "estimated", true, false)]
    public async Task Only_Long_Running_Calls_Become_Tasks(string tool, string flag, bool value, bool expectTask)
    {
        var call = await _client.CallToolAsTaskAsync(
            new CallToolRequestParams
            {
                Name = tool,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["sql"] = JsonSerializer.SerializeToElement(Sql),
                    [flag] = JsonSerializer.SerializeToElement(value),
                },
            },
            CancellationToken);

        Assert.Equal(expectTask, call.IsTask);

        var result = call.IsTask ? await PollToResultAsync(call.TaskCreated!) : call.Result!;

        Assert.True(result.IsError is not true, result.ReadAsText());
    }

    private async Task<CallToolResult> PollToResultAsync(CreateTaskResult created)
    {
        Assert.Equal(2_000, created.PollIntervalMs);

        while (true)
        {
            var task = await _client.GetTaskAsync(created.TaskId, CancellationToken);

            if (task is CompletedTaskResult completed)
            {
                return completed.Result.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions)!;
            }

            Assert.Equal(McpTaskStatus.Working, task.Status);
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
        }
    }
}
