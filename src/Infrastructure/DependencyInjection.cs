using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Infrastructure.Data;
using Deutchezz.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Deutchezz.Infrastructure.Uploads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Deutchezz.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(databaseOptions.DefaultConnection);
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddIdentity<User, Role>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;

                // Uniform credential failures + lockout on repeated wrong passwords (v1.6).
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((jwtOptions, configured) =>
            {
                var settings = configured.Value;
                jwtOptions.MapInboundClaims = false;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                    RoleClaimType = "role",
                    NameClaimType = "sub"
                };
                jwtOptions.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Unauthorized.",
                            Type = "https://httpstatuses.com/401",
                            Detail = "Authentication is required to access this resource."
                        }, options: null, contentType: "application/problem+json");
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Status = StatusCodes.Status403Forbidden,
                            Title = "Forbidden.",
                            Type = "https://httpstatuses.com/403",
                            Detail = "You do not have permission to perform this action."
                        }, options: null, contentType: "application/problem+json");
                    }
                };
            });

        services.AddAuthorization();

        services.AddScoped<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBusinessClock, BusinessClock>();
        services.AddSingleton<ISettingsProvider, AppSettingsProvider>();
        services.AddScoped<IBookingReferenceGenerator, BookingReferenceGenerator>();
        services.AddScoped<IDbConflictDetector, NpgsqlConflictDetector>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<IOtpCodeService, HmacOtpCodeService>();

        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<LoggingEmailSender>();
        services.AddScoped<IEmailSender>(serviceProvider =>
        {
            var email = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;
            return email.Smtp.HasCredentials && !string.IsNullOrWhiteSpace(email.Smtp.FromAddress)
                ? serviceProvider.GetRequiredService<SmtpEmailSender>()
                : serviceProvider.GetRequiredService<LoggingEmailSender>();
        });

        services.Configure<CloudinaryOptions>(configuration.GetSection("Cloudinary"));
        services.AddScoped<Deutchezz.Application.Uploads.IImageUploadService, CloudinaryImageUploadService>();
        return services;
    }
}





