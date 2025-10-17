using Microsoft.Playwright;
using System.Threading.Tasks;

namespace TaskManager.UiTests;

public class PlaywrightConfig
{
    public static async Task<IBrowser> CreateBrowserAsync()
    {
        // Install Playwright browsers if not already installed
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

        var playwright = await Playwright.CreateAsync();
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
                "--disable-web-security",
                "--disable-features=VizDisplayCompositor",
                "--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "--window-size=1920,1080",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--no-first-run",
                "--disable-default-apps",
                "--disable-extensions",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding"
            }
        });
    }

    public static async Task<IBrowserContext> CreateContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            DeviceScaleFactor = 1,
            HasTouch = false,
            IsMobile = false,
            Permissions = new[] { "geolocation", "notifications" }
        });

        // Execute JavaScript to remove webdriver property and set realistic properties
        await context.AddInitScriptAsync(@"
            // Remove webdriver property that Google uses for detection
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined,
            });

            // Override permissions API
            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications' ?
                    Promise.resolve({ state: window.Notification.permission }) :
                    originalQuery(parameters)
            );

            // Mock realistic screen properties
            Object.defineProperty(screen, 'availTop', { value: 0, writable: false });
            Object.defineProperty(screen, 'availLeft', { value: 0, writable: false });
            Object.defineProperty(screen, 'availHeight', { value: 1040, writable: false });
            Object.defineProperty(screen, 'availWidth', { value: 1920, writable: false });

            // Mock plugins to appear more like a real browser
            Object.defineProperty(navigator, 'plugins', {
                get: () => [
                    { name: 'Chrome PDF Plugin', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                    { name: 'Chrome PDF Viewer', description: '', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai' },
                    { name: 'Native Client', description: '', filename: 'internal-nacl-plugin' }
                ]
            });

            // Set realistic hardware concurrency
            Object.defineProperty(navigator, 'hardwareConcurrency', { value: 8, writable: false });
        ");

        return context;
    }
}