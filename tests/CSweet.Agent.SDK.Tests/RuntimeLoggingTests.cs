using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CSweet.Agent.SDK.Tests;

public sealed class RuntimeLoggingTests
{
    [Fact]
    public void FrequentSuccessfulRuntimeCallsDoNotConsumeLogBudgetButFailuresStayVisible()
    {
        var builder = Host.CreateApplicationBuilder();
        var logs = new Capture();
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.AddCSweetAgent<TestAgent>();
        using var host = builder.Build();
        var factory = host.Services.GetRequiredService<ILoggerFactory>();
        var runtime = factory.CreateLogger("System.Net.Http.HttpClient.CSweet.Agent.Runtime.LogicalHandler");
        for (var i = 0; i < 10000; i++) runtime.LogInformation("Successful broker call {Index}", i);
        runtime.LogWarning("Transport warning");
        runtime.LogError("Transport failure");
        factory.CreateLogger("CSweet.Agent.SDK.AgentRuntimeWorker").LogError("Work failure summary");
        factory.CreateLogger("System.Net.Http.HttpClient.Application").LogInformation("Application HTTP call");
        Assert.Equal(new[] { "Transport warning", "Transport failure", "Work failure summary", "Application HTTP call" }, logs.Messages);
    }

    [Fact]
    public void ApplicationCanExplicitlyEnableTransportTracingAfterRegistration()
    {
        var builder = Host.CreateApplicationBuilder();
        var logs = new Capture();
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.AddCSweetAgent<TestAgent>();
        builder.Logging.AddFilter("System.Net.Http.HttpClient.CSweet.Agent.Runtime", LogLevel.Trace);
        using var host = builder.Build();
        host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("System.Net.Http.HttpClient.CSweet.Agent.Runtime.ClientHandler")
            .LogInformation("Explicit diagnostic tracing");
        Assert.Equal("Explicit diagnostic tracing", Assert.Single(logs.Messages));
    }

    private sealed class TestAgent : CSweetAgentBase
    {
        public override string AgentId => "test.runtime-logging";
        public override string Version => "1.0.0";
    }

    private sealed class Capture : ILoggerProvider
    {
        internal List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Sink(Messages);
        public void Dispose() { }
        private sealed class Sink(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, error));
        }
    }
}
