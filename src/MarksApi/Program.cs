using System.Security.Claims;
using System.Text;
using MarksApi.Data;
using MarksApi.Dtos;
using MarksApi.Entities;
using MarksApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ProblemDetailsDto
        {
            Title = "An unhandled server error occurred.",
            Status = 500,
            Detail = "Internal Server Error"
        });
    });
});

app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    if (response.HasStarted || response.ContentLength is > 0 ||
        response.StatusCode < 400 || response.StatusCode > 599)
    {
        return;
    }

    response.ContentType = "application/json";
    var title = response.StatusCode switch
    {
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Resource not found",
        _ => "Request failed"
    };
    await response.WriteAsJsonAsync(new ProblemDetailsDto
    {
        Title = title,
        Status = response.StatusCode,
        Detail = title
    });
});

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseCors("ConfiguredCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapPost("/api/auth/login", async (LoginRequestDto dto, IAuthService auth, ILogger<Program> logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
    {
        return Results.BadRequest(new ProblemDetailsDto
        {
            Title = "Invalid login request",
            Status = 400,
            Detail = "Username and password are required."
        });
    }

    var result = await auth.LoginAsync(dto, ct);
    if (result is null)
    {
        logger.LogWarning("Failed login attempt for username {Username}", dto.Username);
        return Results.Unauthorized();
    }

    logger.LogInformation("Successful login for username {Username} with role {Role}", dto.Username, result.Role);
    return Results.Ok(result);
});

app.MapGet("/api/students", async (int? page, int? pageSize, string? search, string? sort, AppDbContext db, CancellationToken ct) =>
{
    int p = Math.Max(1, page ?? 1);
    int ps = Math.Clamp(pageSize ?? 10, 1, 50);

    var query = db.Students.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(s => s.FullName.Contains(search) || s.RegistrationNo.Contains(search));
    }

    query = sort?.ToLowerInvariant() switch
    {
        "name" => query.OrderBy(s => s.FullName).ThenBy(s => s.Id),
        "email" => query.OrderBy(s => s.Email).ThenBy(s => s.Id),
        _ => query.OrderBy(s => s.Id)
    };

    int total = await query.CountAsync(ct);
    var items = await query.Skip((p - 1) * ps).Take(ps)
        .Select(s => new StudentDto(s.Id, s.RegistrationNo, s.FullName, s.Email))
        .ToListAsync(ct);

    return Results.Ok(new PagedResultDto<StudentDto>(items, p, ps, total));
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapGet("/api/students/{id:int}", async (int id, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
{
    var userRole = ctx.User.FindFirstValue(ClaimTypes.Role);
    var studentIdClaim = ctx.User.FindFirstValue("StudentId");

    if (userRole == "Student" && (studentIdClaim == null || int.Parse(studentIdClaim) != id))
    {
        return Results.Json(new ProblemDetailsDto { Title = "Forbidden", Status = 403, Detail = "Access denied." }, statusCode: 403);
    }

    var student = await db.Students.AsNoTracking()
        .Where(s => s.Id == id)
        .Select(s => new StudentDto(s.Id, s.RegistrationNo, s.FullName, s.Email))
        .FirstOrDefaultAsync(ct);

    return student is null ? Results.NotFound() : Results.Ok(student);
}).RequireAuthorization();

app.MapPost("/api/students", async (CreateStudentDto dto, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Length > 100 ||
        string.IsNullOrWhiteSpace(dto.RegistrationNo) || dto.RegistrationNo.Length > 20 ||
        string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 150 ||
        !dto.Email.Contains('@', StringComparison.Ordinal))
    {
        return Results.BadRequest(new ProblemDetailsDto
        {
            Title = "One or more validation errors occurred.",
            Status = 400,
            Errors = new Dictionary<string, string[]> { { "fullName", new[] { "Full name, reg no, and email are required." } } }
        });
    }

    try
    {
        var student = new Student
        {
            RegistrationNo = dto.RegistrationNo,
            FullName = dto.FullName,
            Email = dto.Email.ToLowerInvariant()
        };
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/students/{student.Id}", new StudentDto(student.Id, student.RegistrationNo, student.FullName, student.Email));
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        return Results.Conflict(new ProblemDetailsDto
        {
            Title = "Duplicate student",
            Status = 409,
            Detail = "A student with this registration number or email already exists."
        });
    }
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapPut("/api/students/{id:int}", async (int id, UpdateStudentDto dto, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Length > 100 ||
        string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 150 ||
        !dto.Email.Contains('@', StringComparison.Ordinal))
    {
        return Results.BadRequest(new ProblemDetailsDto { Title = "Invalid student", Status = 400, Detail = "Full name and a valid email are required." });
    }

    var student = await db.Students.FindAsync(new object[] { id }, ct);
    if (student == null) return Results.NotFound();

    student.FullName = dto.FullName;
    student.Email = dto.Email.ToLowerInvariant();
    try
    {
        await db.SaveChangesAsync(ct);
        return Results.Ok(new StudentDto(student.Id, student.RegistrationNo, student.FullName, student.Email));
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Duplicate data", Status = 409, Detail = "Email conflict." });
    }
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapDelete("/api/students/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
{
    var student = await db.Students.Include(s => s.Enrollments).FirstOrDefaultAsync(s => s.Id == id, ct);
    if (student == null) return Results.NotFound();
    if (student.Enrollments.Any())
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Cannot delete student", Status = 409, Detail = "Student has active enrollments." });
    }

    db.Students.Remove(student);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapGet("/api/courses", async (int? page, int? pageSize, string? search, string? sort, AppDbContext db, CancellationToken ct) =>
{
    int p = Math.Max(1, page ?? 1);
    int ps = Math.Clamp(pageSize ?? 10, 1, 50);
    var query = db.Courses.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(c => c.Code.Contains(search) || c.Title.Contains(search));
    }

    query = sort?.ToLowerInvariant() switch
    {
        "code" => query.OrderBy(c => c.Code).ThenBy(c => c.Id),
        "title" => query.OrderBy(c => c.Title).ThenBy(c => c.Id),
        _ => query.OrderBy(c => c.Id)
    };

    int total = await query.CountAsync(ct);
    var courses = await query.Skip((p - 1) * ps).Take(ps)
        .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.CreditHours))
        .ToListAsync(ct);
    return Results.Ok(new PagedResultDto<CourseDto>(courses, p, ps, total));
}).RequireAuthorization();

app.MapGet("/api/courses/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
{
    var course = await db.Courses.AsNoTracking()
        .Where(c => c.Id == id)
        .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.CreditHours))
        .FirstOrDefaultAsync(ct);
    return course is null ? Results.NotFound() : Results.Ok(course);
}).RequireAuthorization();

app.MapPost("/api/courses", async (CreateCourseDto dto, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Code) || dto.Code.Length > 10 ||
        string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Length > 150 ||
        dto.CreditHours < 1 || dto.CreditHours > 4)
    {
        return Results.BadRequest(new ProblemDetailsDto { Title = "Invalid credit hours", Status = 400, Detail = "Credit hours must be 1 to 4." });
    }

    try
    {
        var course = new Course { Code = dto.Code, Title = dto.Title, CreditHours = dto.CreditHours };
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/courses/{course.Id}", new CourseDto(course.Id, course.Code, course.Title, course.CreditHours));
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Duplicate course", Status = 409, Detail = "Course code already exists." });
    }
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapPut("/api/courses/{id:int}", async (int id, UpdateCourseDto dto, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Length > 150 ||
        dto.CreditHours < 1 || dto.CreditHours > 4)
    {
        return Results.BadRequest(new ProblemDetailsDto { Title = "Invalid course", Status = 400, Detail = "Title and credit hours from 1 to 4 are required." });
    }

    var course = await db.Courses.FindAsync(new object[] { id }, ct);
    if (course is null) return Results.NotFound();

    course.Title = dto.Title;
    course.CreditHours = dto.CreditHours;
    try
    {
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CourseDto(course.Id, course.Code, course.Title, course.CreditHours));
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Course conflict", Status = 409, Detail = "Course data conflicts with an existing record." });
    }
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapDelete("/api/courses/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
{
    var course = await db.Courses.Include(c => c.Enrollments).FirstOrDefaultAsync(c => c.Id == id, ct);
    if (course is null) return Results.NotFound();
    if (course.Enrollments.Count > 0)
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Cannot delete course", Status = 409, Detail = "Course has active enrollments." });
    }

    db.Courses.Remove(course);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapPost("/api/courses/{courseId:int}/enrollments", async (int courseId, int studentId, AppDbContext db, CancellationToken ct) =>
{
    var courseExists = await db.Courses.AnyAsync(c => c.Id == courseId, ct);
    var studentExists = await db.Students.AnyAsync(s => s.Id == studentId, ct);
    if (!courseExists || !studentExists)
    {
        return Results.NotFound(new ProblemDetailsDto
        {
            Title = "Resource not found",
            Status = 404,
            Detail = "The specified course or student does not exist."
        });
    }

    try
    {
        var enrollment = new Enrollment { CourseId = courseId, StudentId = studentId };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/enrollments/{enrollment.Id}", new EnrollmentDto(enrollment.Id, enrollment.StudentId, enrollment.CourseId, enrollment.Mark));
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        return Results.Conflict(new ProblemDetailsDto { Title = "Enrollment Conflict", Status = 409, Detail = "Student is already enrolled." });
    }
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapGet("/api/courses/{courseId:int}/enrollments", async (int courseId, AppDbContext db, CancellationToken ct) =>
{
    var courseExists = await db.Courses.AnyAsync(c => c.Id == courseId, ct);
    if (!courseExists) return Results.NotFound();

    var enrollments = await db.Enrollments.AsNoTracking()
        .Where(e => e.CourseId == courseId)
        .OrderBy(e => e.Id)
        .Select(e => new EnrollmentDto(e.Id, e.StudentId, e.CourseId, e.Mark))
        .ToListAsync(ct);
    return Results.Ok(enrollments);
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapPut("/api/enrollments/{id:int}/mark", async (int id, UpdateMarkDto dto, AppDbContext db, CancellationToken ct) =>
{
    if (dto.Mark < 0 || dto.Mark > 100)
    {
        return Results.BadRequest(new ProblemDetailsDto { Title = "Invalid mark", Status = 400, Detail = "Mark must be between 0 and 100." });
    }

    var enrollment = await db.Enrollments.FindAsync(new object[] { id }, ct);
    if (enrollment == null) return Results.NotFound();

    enrollment.Mark = dto.Mark;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new EnrollmentDto(enrollment.Id, enrollment.StudentId, enrollment.CourseId, enrollment.Mark));
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapDelete("/api/enrollments/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
{
    var enrollment = await db.Enrollments.FindAsync(new object[] { id }, ct);
    if (enrollment is null) return Results.NotFound();

    db.Enrollments.Remove(enrollment);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapGet("/api/courses/{courseId:int}/stats", async (int courseId, AppDbContext db, IConfiguration config, CancellationToken ct) =>
{
    var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, ct);
    if (course == null) return Results.NotFound();

    var enrollments = await db.Enrollments.AsNoTracking().Where(e => e.CourseId == courseId).ToListAsync(ct);
    int passMark = int.Parse(config["Grading:PassMark"] ?? "50");

    int totalEnrolled = enrollments.Count;
    var graded = enrollments.Where(e => e.Mark.HasValue).Select(e => e.Mark!.Value).ToList();

    double? avg = graded.Count > 0 ? graded.Average() : null;
    int? highest = graded.Count > 0 ? graded.Max() : null;
    int? lowest = graded.Count > 0 ? graded.Min() : null;
    int passed = graded.Count(m => m >= passMark);

    return Results.Ok(new CourseStatsDto(course.Id, course.Code, totalEnrolled, graded.Count, avg, highest, lowest, passed));
}).RequireAuthorization(p => p.RequireRole("Teacher"));

app.MapGet("/api/students/{id:int}/transcript", async (int id, HttpContext ctx, AppDbContext db, IConfiguration config, CancellationToken ct) =>
{
    var userRole = ctx.User.FindFirstValue(ClaimTypes.Role);
    var studentIdClaim = ctx.User.FindFirstValue("StudentId");

    if (userRole == "Student" && (studentIdClaim == null || int.Parse(studentIdClaim) != id))
    {
        return Results.Json(new ProblemDetailsDto { Title = "Forbidden", Status = 403, Detail = "Access denied." }, statusCode: 403);
    }

    var student = await db.Students.AsNoTracking().Include(s => s.Enrollments).ThenInclude(e => e.Course).FirstOrDefaultAsync(s => s.Id == id, ct);
    if (student == null) return Results.NotFound();

    int passMark = int.Parse(config["Grading:PassMark"] ?? "50");

    var courseDtos = new List<TranscriptCourseDto>();
    double totalWeighted = 0;
    int totalCredits = 0;

    foreach (var e in student.Enrollments)
    {
        bool passed = e.Mark.HasValue && e.Mark.Value >= passMark;
        courseDtos.Add(new TranscriptCourseDto(e.Course.Code, e.Course.CreditHours, e.Mark, passed));

        if (e.Mark.HasValue)
        {
            totalWeighted += e.Mark.Value * e.Course.CreditHours;
            totalCredits += e.Course.CreditHours;
        }
    }

    double? weightedAvg = totalCredits > 0 ? totalWeighted / totalCredits : null;
    return Results.Ok(new TranscriptDto(student.Id, student.RegistrationNo, student.FullName, courseDtos, weightedAvg));
}).RequireAuthorization();

if (args.Contains("--smoke-test"))
{
    await app.StartAsync();
    var serverUrl = app.Urls.FirstOrDefault(u => u.StartsWith("http://")) ?? app.Urls.FirstOrDefault() ?? "http://localhost:5011";
    bool success = await MarksApi.SmokeTestRunner.RunAsync(serverUrl, builder.Configuration);
    await app.StopAsync();
    Environment.Exit(success ? 0 : 1);
}

static bool IsUniqueViolation(DbUpdateException exception) =>
    exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

app.Run();