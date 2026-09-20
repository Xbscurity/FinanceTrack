using api.Authorization;
using api.Constants;
using api.Data;
using api.Middlewares;
using api.Models;
using api.Options;
using api.Providers.ClientIpProvider;
using api.Providers.CurrentUser;
using api.Repositories;
using api.Services.Background;
using api.Services.RefreshTokenCookie;
using api.Services.User;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;

namespace api
{
    /// <summary>
    /// Provides extension methods for registering infrastructure-layer services.
    /// </summary>
    public static class InfrastructureServiceCollectionExtensions
    {
        /// <summary>
        /// Registers infrastructure services including persistence, authentication, authorization,
        /// caching, logging, and background processing.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

            services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(services));

            services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();

            services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

            services.AddScoped<IFinancialTransactionRepository, FinancialTransactionRepository>();
            services.AddScoped<ITokenRepository, TokenRepository>();

            services.AddScoped<IRefreshTokenCookieService, RefreshTokenCookieService>();

            services.AddHttpContextAccessor();

            services.AddScoped<ICurrentUser, CurrentUser>();

            services.AddScoped<IClientIpProvider, ClientIpProvider>();

            services.AddTransient<LogEnrichmentMiddleware>();

            services.AddScoped<IUserService, UserService>();

            services.AddFusionCache();

            services.AddHostedService<RefreshTokenCleanupService>();

            services.AddHealthChecks();
            services.AddExceptionHandler<GlobalExceptionHandler>();

            services.AddScoped<IAuthorizationHandler, NotBannedHandler>();

            services.AddAuthorizationBuilder()
                .AddPolicy(Policies.NotBanned, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new NotBannedRequirement());
                });

            var jwt = configuration.GetRequiredSection(JwtOptions.SectionName).Get<JwtOptions>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = JwtRegisteredClaimNames.Name,
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        RequireSignedTokens = true,
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha512 },
                    };
                });
            services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 8;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

            services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
                };
            });

            var rateLimitingOptions = configuration
                .GetRequiredSection(RateLimitingOptions.SectionName)
                .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var retryAfterSeconds = 60d;

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        retryAfterSeconds = retryAfter.TotalSeconds;
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfterSeconds)).ToString();
                    }

                    Dictionary<string, object?> extensions = new()
                    {
                        ["errorCode"] = "TOO_MANY_REQUESTS",
                        ["retryAfterSeconds"] = retryAfterSeconds,
                    };

                    await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        detail: "Rate limit exceeded. Please try again later.",
                        instance: context.HttpContext.Request.Path,
                        extensions: extensions)
                        .ExecuteAsync(context.HttpContext);
                };

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>

                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.User.Identity?.Name
                      ?? httpContext.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = rateLimitingOptions.Global.PermitLimit,
                            Window = TimeSpan.FromSeconds(rateLimitingOptions.Global.WindowSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }));

                options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitingOptions.Auth.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitingOptions.Auth.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            });

            services.AddOptions<JwtOptions>()
                .BindConfiguration(JwtOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<SeedOptions>()
                .BindConfiguration(SeedOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<CacheOptions>()
                .BindConfiguration(CacheOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services
                .AddOptions<RateLimitingOptions>()
                .BindConfiguration(RateLimitingOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            return services;
        }
    }
}
