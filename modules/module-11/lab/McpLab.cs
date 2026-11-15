using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

static class McpLab
{
    public static async Task ServeAsync()
    {
        var host = Host.CreateApplicationBuilder();
        host.Logging.ClearProviders();
        host.Services.AddMcpServer().WithStdioServerTransport().WithTools<OrderMcpTools>();
        await host.Build().RunAsync();
    }

    public static async Task RunAsync(Action<string, object> log)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new()
            {
                Name = "local-order-server",
                Command = "dotnet",
                Arguments = [Assembly.GetExecutingAssembly().Location, "mcp-server"]
            }), cancellationToken: deadline.Token);

        var tools = await client.ListToolsAsync(cancellationToken: deadline.Token);
        var tool = tools.Single(item => item.Name == "lookup_order");
        log("discovery", new { tool.Name, tool.Description, tool.JsonSchema });
        foreach (var (id, expected) in new[] { ("ORD-1042", "ok"), ("ORD-2045", "not_available") })
        {
            var reply = await client.CallToolAsync(tool.Name, new Dictionary<string, object?> { ["orderId"] = id },
                cancellationToken: deadline.Token);
            var result = JsonSerializer.Deserialize<OrderResult>(reply.Content.OfType<TextContentBlock>().Single().Text);
            if (result?.Code != expected || (reply.IsError == true) != (expected != "ok"))
                throw new InvalidOperationException("MCP result differs from the fixture.");
            log(expected, new { arguments = new { orderId = id }, reply.IsError, result });
        }
    }
}

[McpServerToolType]
sealed class OrderMcpTools
{
    [McpServerTool(Name = "lookup_order", ReadOnly = true)]
    [Description("Get order status for the caller bound to this local server.")]
    public static CallToolResult LookupOrder([Description("Order identifier such as ORD-1042.")] string orderId)
    {
        // In this local fixture only, the server is bound to Alex at startup.
        var result = Orders.Lookup(new("alex"), new(orderId));
        return new()
        {
            IsError = result.Code != "ok",
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result) }]
        };
    }
}

