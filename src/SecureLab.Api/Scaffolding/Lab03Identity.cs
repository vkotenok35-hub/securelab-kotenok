using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
	    private const string AllowedUserNameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
		
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
		        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                    .LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
                        "login", "rate-limited", context.HttpContext.TraceIdentifier);
                return ValueTask.CompletedTask;
            };
            // Ключ — IP клієнта; ліміт лише для login. Тестовий host не має IP (ключ "unknown").
            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromSeconds(15),
                        QueueLimit = 0
                    }));
        });
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
                app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            // Нормалізуємо лише Trim() цих трьох полів; password не нормалізуємо, не журналюємо, не повертаємо.
            var userName = input.UserName?.Trim() ?? "";
            var email = input.Email?.Trim() ?? "";
            var displayName = input.DisplayName?.Trim() ?? "";
            var errors = new Dictionary<string, string[]>();

            if (userName.Length == 0)
                errors["userName"] = ["Ім'я користувача обов'язкове."];
            else if (userName.Length > 256)
                errors["userName"] = ["Ім'я користувача не може перевищувати 256 символів."];
            else if (!userName.All(c => AllowedUserNameChars.Contains(c)))
                errors["userName"] = ["Дозволені лише латинські літери, цифри та символи -._@+"];

            if (email.Length == 0)
                errors["email"] = ["Email обов'язковий."];
            else if (email.Length > 256)
                errors["email"] = ["Email не може перевищувати 256 символів."];
            else if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
                errors["email"] = ["Некоректний формат email."];

            if (displayName.Length == 0)
                errors["displayName"] = ["Ім'я профілю обов'язкове."];
            else if (displayName.Length > 120)
                errors["displayName"] = ["Ім'я профілю не може перевищувати 120 символів."];

            if (string.IsNullOrEmpty(input.Password))
                errors["password"] = ["Пароль обов'язковий."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = userName, Email = email, DisplayName = displayName
            };
            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
            {
                var codes = result.Errors.Select(error => error.Code).ToHashSet();
                // Дублікат — конфлікт зі станом даних: 409, без уточнення, яке саме поле зайняте.
                if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
                    return Results.Problem(
                        title: "Реєстрація неможлива",
                        detail: "Обліковий запис із такими даними вже існує.",
                        statusCode: StatusCodes.Status409Conflict);
                // Решта очікуваних помилок Identity — некоректні дані: 400 без пароля й повного request.
                var safe = new Dictionary<string, string[]>();
                if (codes.Any(code => code.StartsWith("Password")))
                    safe["password"] = ["Пароль не відповідає політиці: щонайменше 10 символів, велика й мала латинські літери, цифра та символ."];
                if (codes.Contains("InvalidUserName"))
                    safe["userName"] = ["Недопустиме ім'я користувача."];
                if (codes.Contains("InvalidEmail"))
                    safe["email"] = ["Некоректний формат email."];
                if (safe.Count == 0)
                    safe["registration"] = ["Реєстрація не виконана."];
                return Results.ValidationProblem(safe);
            }
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            return Results.Created("/api/me", new RegistrationResponse(user.Id, userName, displayName));
        });
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn,
            HttpContext httpContext, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
                    "login", "invalid-input", httpContext.TraceIdentifier);
                return Results.Unauthorized();
            }
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
                "login", result.Succeeded ? "success" : "failure", httpContext.TraceIdentifier);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("login");
		
		app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn,
            HttpContext httpContext, ILogger<Program> logger) =>
        {
            await signIn.SignOutAsync();
            logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
                "logout", "success", httpContext.TraceIdentifier);
            return Results.NoContent();
        });
                // Користувач береться лише з перевіреного principal (cookie), а не з header чи тіла запиту.
        app.MapGet("/api/me", async (HttpContext context, SecureLabDbContext db) =>
        {
            var idValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idValue, out var userId)) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new MeResponse(user.Id, user.UserName, user.DisplayName));
        }).RequireAuthorization();
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);
public sealed record MeResponse(Guid Id, string? UserName, string DisplayName);