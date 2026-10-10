using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Infrastructure.Data;
using Deutchezz.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
            });

        services.AddAuthorization();

        services.AddScoped<IClock, SystemClock>();
        services.AddSingleton<IBusinessClock, BusinessClock>();
        services.AddScoped<IBookingReferenceGenerator, BookingReferenceGenerator>();
        services.AddScoped<IEmailSender, EmailSender>();
        services.AddScoped<IDbConflictDetector, NpgsqlConflictDetector>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITokenService, TokenService>();

        return services;
    }
}