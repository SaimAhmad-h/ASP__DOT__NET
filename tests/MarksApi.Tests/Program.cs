using MarksApi.Data;
using MarksApi.Dtos;
using MarksApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database before running tests.");

await using var db = new AppDbContext(
    new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

await db.Database.CanConnectAsync();
var service = new AuthService(db, new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = "test-signing-key-with-at-least-32-characters",
        ["Jwt:Issuer"] = "MarksApi",
        ["Jwt:Audience"] = "MarksApiUsers",
        ["Jwt:ExpiryMinutes"] = "60"
    })
    .Build());

var validLogin = await service.LoginAsync(
    new LoginRequestDto("teacher1", "Teacher123!"), CancellationToken.None);
Assert(validLogin is not null, "teacher credentials should authenticate");
Assert(validLogin!.Role == "Teacher", "teacher role should be returned");

var databasePasswordLogin = await service.LoginAsync(
    new LoginRequestDto("teacher1", "4433"), CancellationToken.None);
Assert(databasePasswordLogin is null, "database password must not authenticate seeded users");

var invalidLogin = await service.LoginAsync(
    new LoginRequestDto("teacher1", "wrong-password"), CancellationToken.None);
Assert(invalidLogin is null, "invalid user password must be rejected");

Console.WriteLine("All authentication tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
