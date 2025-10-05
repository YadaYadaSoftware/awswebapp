using Microsoft.Extensions.Configuration;
using System.IO;

namespace TaskManager.UiTests;

public class TestConfiguration
{
    private static TestConfiguration? _instance;
    private static readonly object _lock = new object();

    public string BaseUrl { get; }
    public string LoginPath { get; }
    public string GoogleLoginUrl { get; }
    public int TestTimeout { get; }
    public int RetryAttempts { get; }

    private TestConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var testSettings = configuration.GetSection("TestSettings");

        BaseUrl = testSettings["BaseUrl"] ?? "https://dev.appcloud.systems";
        LoginPath = testSettings["LoginPath"] ?? "/Identity/Account/Login";
        GoogleLoginUrl = testSettings["GoogleLoginUrl"] ?? "https://accounts.google.com/v3/signin/identifier";
        TestTimeout = int.Parse(testSettings["TestTimeout"] ?? "30000");
        RetryAttempts = int.Parse(testSettings["RetryAttempts"] ?? "3");
    }

    public static TestConfiguration Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new TestConfiguration();
                    }
                }
            }
            return _instance;
        }
    }
}