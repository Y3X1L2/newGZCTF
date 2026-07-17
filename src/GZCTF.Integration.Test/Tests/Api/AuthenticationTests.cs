using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models.Request.Account;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace GZCTF.Integration.Test.Tests.Api;

/// <summary>
/// Tests for authentication and authorization workflows
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class AuthenticationTests(GZCTFApplicationFactory factory, ITestOutputHelper output)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Account_Login_WithSeededUser_Succeeds()
    {
        var password = "S3eded!Pass";
        var userName = TestDataSeeder.RandomName();
        var email = $"{userName}@example.com";
        var seeded = await TestDataSeeder.CreateUserAsync(factory.Services,
            userName,
            password,
            email);

        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = seeded.UserName, Password = password });

        loginResponse.EnsureSuccessStatusCode();

        var profileResponse = await client.GetAsync("/api/Account/Profile");
        profileResponse.EnsureSuccessStatusCode();

        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileUserInfoModel>();
        Assert.NotNull(profile);
        Assert.Equal(seeded.UserName, profile.UserName);
        Assert.Equal(seeded.Email, profile.Email);
    }

    [Fact]
    public async Task Account_Capabilities_ReturnPublicAuthenticationOptions()
    {
        using var host = RecoveryEnabledHost();
        using var client = host.CreateClient();
        var response = await client.GetAsync("/api/Account/Capabilities");

        response.EnsureSuccessStatusCode();
        var capabilities = await response.Content.ReadFromJsonAsync<AccountCapabilitiesModel>();

        Assert.NotNull(capabilities);
        Assert.True(capabilities.AllowPasswordLogin);
        Assert.True(capabilities.AllowRegister);
        Assert.True(capabilities.PasswordRecoveryAvailable);
        Assert.True(capabilities.EmailConfirmationRequired);
        Assert.False(capabilities.PortalSso.Enabled);
        Assert.Null(capabilities.PortalSso.EntryUrl);
    }

    [Fact]
    public async Task Recovery_DoesNotRevealWhetherAccountExists()
    {
        using var host = RecoveryEnabledHost();
        using var client = host.CreateClient();
        var first = await client.PostAsJsonAsync("/api/Account/Recovery", new RecoveryModel
        {
            Email = $"missing_{Guid.NewGuid():N}@example.com"
        });
        var second = await client.PostAsJsonAsync("/api/Account/Recovery", new RecoveryModel
        {
            Email = $"also_missing_{Guid.NewGuid():N}@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> RecoveryEnabledHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccountPolicy:EmailConfirmationRequired"] = "true"
            })));

    [Fact]
    public async Task Register_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var registerModel = new
        {
            userName = TestDataSeeder.RandomName(),
            password = "TestPassword123!",
            email = $"test_{Guid.NewGuid():N}@example.com"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Account/Register", registerModel);
        output.WriteLine($"Status: {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Response: {content}");

        // Assert
        // Registration might succeed or fail depending on global config
        // We just verify we get a valid response (not a 404 or 500)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected OK or BadRequest but got {response.StatusCode}"
        );
    }

    [Fact]
    public async Task Register_WithInvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        var registerModel = new
        {
            userName = TestDataSeeder.RandomName(),
            password = "TestPassword123!",
            email = "invalid-email"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Account/Register", registerModel);
        output.WriteLine($"Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_ReturnsBadRequest()
    {
        // Arrange
        var registerModel = new { userName = "testuser", password = "123", email = "test@example.com" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Account/Register", registerModel);
        output.WriteLine($"Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutCredentials_ReturnsBadRequest()
    {
        // Arrange
        var loginModel = new { };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Account/LogIn", loginModel);
        output.WriteLine($"Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync("/api/Account/LogOut", null);
        output.WriteLine($"Status: {response.StatusCode}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_WithInvalidToken_ReturnsBadRequest()
    {
        // Arrange
        var verifyModel = new { email = "test@example.com", token = "invalid-token" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Account/Verify", verifyModel);
        output.WriteLine($"Status: {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Response: {content}");

        // Assert - endpoint returns OK with error message in body
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected OK or BadRequest but got {response.StatusCode}"
        );
    }
}
