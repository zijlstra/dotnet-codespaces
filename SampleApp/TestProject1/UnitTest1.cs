using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TestProject1;

public class UnitTest1 : IClassFixture<WeatherForecastApiFixture>
{
    private readonly HttpClient _client;

    public UnitTest1(WeatherForecastApiFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task GetWeatherForecast_ReturnsFiveValidForecasts()
    {
        using var response = await _client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var forecasts = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(5, forecasts.Length);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var allowedSummaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild",
            "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        for (var index = 0; index < forecasts.Length; index++)
        {
            var forecast = forecasts[index];
            var expectedDate = today.AddDays(index + 1);

            Assert.Equal(
                expectedDate,
                DateOnly.Parse(forecast.GetProperty("date").GetString()!));

            var temperatureC = forecast.GetProperty("temperatureC").GetInt32();
            Assert.InRange(temperatureC, -20, 54);

            var summary = forecast.GetProperty("summary").GetString();
            Assert.Contains(summary, allowedSummaries);

            var expectedFahrenheit = 32 + (int)(temperatureC / 0.5556);
            Assert.Equal(
                expectedFahrenheit,
                forecast.GetProperty("temperatureF").GetInt32());
        }
    }

    [Fact]
    public async Task GetUnknownRoute_ReturnsNotFound()
    {
        using var response = await _client.GetAsync("/not-a-weather-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostWeatherForecast_ReturnsMethodNotAllowed()
    {
        using var response = await _client.PostAsync(
            "/weatherforecast",
            JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}

public sealed class WeatherForecastApiFixture : IAsyncLifetime
{
    private Process? _process;
    private readonly string _baseAddress =
        $"http://127.0.0.1:{GetAvailablePort()}";

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var projectPath = FindBackendProject();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);

        startInfo.Environment["ASPNETCORE_URLS"] = _baseAddress;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the backend.");

        Client = new HttpClient { BaseAddress = new Uri(_baseAddress) };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        while (!timeout.IsCancellationRequested)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Backend exited unexpectedly with code {_process.ExitCode}.");
            }

            try
            {
                using var response = await Client.GetAsync(
                    "/weatherforecast",
                    timeout.Token);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The server may not be listening yet.
            }

            await Task.Delay(250, timeout.Token);
        }

        throw new TimeoutException("Backend did not start within 45 seconds.");
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }

        _process?.Dispose();
        return Task.CompletedTask;
    }

    private static string FindBackendProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SampleApp",
                "BackEnd",
                "BackEnd.csproj");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Could not locate SampleApp/BackEnd/BackEnd.csproj.");
    }

    private static int GetAvailablePort()
    {
        using var listener =
            new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}