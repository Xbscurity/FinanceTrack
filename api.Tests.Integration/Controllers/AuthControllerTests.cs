using api.Constants;
using api.Data;
using api.Dtos.User;
using api.Models;
using api.Tests.Integration.Collections.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace api.Tests.Integration.Controllers
{
    [Collection("IntegrationTestCollection")]
    public class AuthControllerTests : IAsyncLifetime
    {
        private const string ExistingUserName = "loginuser";
        private const string ExistingEmail = "loginuser@test.com";
        private const string ExistingPassword = "ValidPassword123!";

        private string _userId = null!;

        private readonly IntegrationTestFixture _fixture;

        public async ValueTask InitializeAsync()
        {
            await _fixture.ResetCheckpointAsync();

            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                UserName = ExistingUserName,
                Email = ExistingEmail,
            };

            var createResult = await userManager.CreateAsync(user, ExistingPassword);
            Assert.True(createResult.Succeeded);

            _userId = user.Id;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public AuthControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Register_WithValidData_Returns200AndSetsRefreshTokenCookie()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            var input = new RegisterInputDto
            {
                UserName = "test",
                Email = "test@example.com",
                Password = "Password123",
            };

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/auth/register",
                input,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<AuthOutputDto>(TestContext.Current.CancellationToken);
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
            Assert.Equal(input.Email, result.Email);
            Assert.Equal(input.UserName, result.UserName);
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
            Assert.Contains(cookies!, c => c.StartsWith(CookieNames.RefreshToken, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Register_InvalidInput_Returns422UnprocessableEntity()
        {
            using var client = _fixture.CreateAnonymousClient();

            var input = new RegisterInputDto
            {
                UserName = "test",
                Email = "test@example.com",
                Password = "short",
            };

            var response = await client.PostAsJsonAsync(
                "/api/auth/register",
                input,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
                cancellationToken: TestContext.Current.CancellationToken);

            var root = doc.RootElement;
            Assert.Equal(StatusCodes.Status422UnprocessableEntity, root.GetProperty("status").GetInt32());

            var errors = root.GetProperty("errors");
            Assert.True(errors.TryGetProperty("password", out var passwordErrors));

            var codes = passwordErrors.EnumerateArray()
                .Select(e => e.GetProperty("code").GetString())
                .ToList();

            Assert.Contains("AUTH_PASSWORD_TOO_SHORT", codes);
        }

        [Fact]
        public async Task Register_BadInput_Returns400BadRequest()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/auth/register",
                (RegisterInputDto?)null,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Register_DuplicateUserName_Returns409Conflict()
        {
            // Arrange
            const string userName = "existinguser";

            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider
                    .GetRequiredService<UserManager<AppUser>>();

                var existingUser = new AppUser
                {
                    UserName = userName,
                    Email = "existing@test.com"
                };

                var createResult = await userManager.CreateAsync(existingUser, "ValidPassword123!");
                Assert.True(createResult.Succeeded);
            }

            using var client = _fixture.CreateAnonymousClient();

            var input = new RegisterInputDto
            {
                UserName = userName,
                Email = "different@test.com",
                Password = "ValidPassword123!",
            };

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/auth/register",
                input,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Login_WithValidCredentials_Returns200AndSetsRefreshTokenCookie()
        {
            // Arrange
            var input = new LoginInputDto
            {
                UserName = ExistingUserName,
                Password = ExistingPassword,
            };

            using var client = _fixture.CreateAnonymousClient();

            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                input,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Act
            var body = await response.Content.ReadFromJsonAsync<AuthOutputDto>(
                TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(body);
            Assert.Equal(ExistingEmail, body!.Email);
            Assert.Equal(ExistingUserName, body.UserName);
            Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));

            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
            Assert.Contains(cookies!, c => c.StartsWith(CookieNames.RefreshToken, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_Returns401Unauthorized()
        {
            // Arrange
            var input = new LoginInputDto
            {
                UserName = ExistingUserName,
                Password = "invalid",
            };

            using var client = _fixture.CreateAnonymousClient();

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                input,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        }

        [Fact]
        public async Task Login_BadInput_Returns400BadRequest()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                (LoginInputDto?)null,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        }

        [Fact]
        public async Task Refresh_WithValidRefreshTokenCookie_Returns200AndNewCookie()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            var loginResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginInputDto { UserName = ExistingUserName, Password = ExistingPassword },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            // Act
            var response = await client.PostAsync(
                "/api/auth/refresh",
                content: null,
                TestContext.Current.CancellationToken);

            // Assert    
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<RefreshTokenOutputDto>(
                TestContext.Current.CancellationToken);

            Assert.NotNull(body);
            Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));

            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));

            loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders);
            Assert.Contains(cookies!, c => c.StartsWith(CookieNames.RefreshToken, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refresh_WithoutCookie_Returns401Unauthorized()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            // Act
            var response = await client.PostAsync(
                "/api/auth/refresh",
                content: null,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Refresh_WithInvalidCookieValue_Returns401Unauthorized()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            request.Headers.Add("Cookie", $"{CookieNames.RefreshToken}=not-a-real-token");

            // Act
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Logout_WithValidRefreshTokenCookie_Returns204AndRevokesToken()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            var loginResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginInputDto { UserName = ExistingUserName, Password = ExistingPassword },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            // Act
            var response = await client.PostAsync(
                "/api/auth/logout",
                content: null,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
            Assert.Contains(cookies!, c => c.StartsWith(CookieNames.RefreshToken, StringComparison.Ordinal));

            using var assertScope = _fixture.Factory.Services.CreateScope();
            var context = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tokens = await context.RefreshTokens
                .Where(t => t.UserId == _userId)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.NotEmpty(tokens);
            Assert.All(tokens, t => Assert.True(t.IsRevoked));
        }

        [Fact]
        public async Task Logout_WithoutCookie_Returns204AndDoesNotTouchTokens()
        {
            // Arrange
            using var client = _fixture.CreateAnonymousClient();

            // Act
            var response = await client.PostAsync(
                "/api/auth/logout",
                content: null,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        }
    }
}