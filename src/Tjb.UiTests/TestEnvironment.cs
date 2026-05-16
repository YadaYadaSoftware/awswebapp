using System;
using System.Collections.Generic;

namespace Tjb.UiTests;

public class TestEnvironment
{
    public string Name { get; }
    public string BaseUrl { get; }
    public bool RequiresAuthentication { get; }
    public Dictionary<string, string> EnvironmentVariables { get; }

    private TestEnvironment(string name, string baseUrl, bool requiresAuthentication)
    {
        Name = name;
        BaseUrl = baseUrl;
        RequiresAuthentication = requiresAuthentication;
        EnvironmentVariables = new Dictionary<string, string>();
    }

    public static TestEnvironment Development { get; } = new TestEnvironment(
        "Development",
        "https://dev.appcloud.systems",
        false
    );

    public static TestEnvironment Staging { get; } = new TestEnvironment(
        "Staging",
        "https://staging.appcloud.systems",
        true
    );

    public static TestEnvironment Production { get; } = new TestEnvironment(
        "Production",
        "https://appcloud.systems",
        true
    );

    public static TestEnvironment GetCurrent()
    {
        var environmentName = Environment.GetEnvironmentVariable("TEST_ENVIRONMENT") ?? "Development";
        return environmentName.ToLower() switch
        {
            "production" => Production,
            "staging" => Staging,
            _ => Development
        };
    }

    public void ConfigureTestSettings()
    {
        // Update configuration with environment-specific settings
        var config = TestConfiguration.Instance;

        // Set environment variables for this test run
        foreach (var kvp in EnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }
}