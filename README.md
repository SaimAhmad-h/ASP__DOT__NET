# Marks API

Marks API is an ASP.NET Core Web API for managing students, courses, enrollments,
marks, transcripts, and role-based authentication. It uses PostgreSQL through
Entity Framework Core migrations.

## Requirements

- .NET SDK 10
- PostgreSQL 16 or newer
- Postman

## Database setup

The database login password is `4433`. This is only the PostgreSQL credential. It
is not used as an application user password.

```sql
CREATE USER marks_user WITH PASSWORD '4433';
CREATE DATABASE marks_db OWNER marks_user;
```

If the role or database already exists, skip the corresponding statement.

## Configuration and secrets

Copy `appsettings.Example.json` to a secure local configuration source. Do not
commit a real connection string or JWT key. From `src/MarksApi`, configure user
secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Database" \
  "Host=127.0.0.1;Port=5432;Database=marks_db;Username=marks_user;Password=4433"
dotnet user-secrets set "Jwt:Key" \
  "replace-this-with-a-random-secret-at-least-32-characters"
dotnet user-secrets set "SmokeTest:TeacherUsername" "teacher1"
dotnet user-secrets set "SmokeTest:TeacherPassword" "Teacher123!"
dotnet user-secrets set "SmokeTest:StudentUsername" "student1"
dotnet user-secrets set "SmokeTest:StudentPassword" "Student123!"
```

For a server, use environment variables with double underscores instead:
`ConnectionStrings__Database`, `Jwt__Key`, `SmokeTest__TeacherUsername`,
`SmokeTest__TeacherPassword`, `SmokeTest__StudentUsername`, and
`SmokeTest__StudentPassword`.

## Build, migrate, and run

```bash
dotnet restore
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/MarksApi/MarksApi.csproj
dotnet build src/MarksApi/MarksApi.csproj
dotnet run --project src/MarksApi/MarksApi.csproj
```

The API uses the configured development URLs. Swagger UI is available at
`/swagger`, and the database-backed health check is available at `/health`.

## Development-only seeded accounts

| Username | Password | Role |
| --- | --- | --- |
| `teacher1` | `Teacher123!` | Teacher |
| `student1` | `Student123!` | Student |
| `student2` | `Student123!` | Student |

These credentials are for development and demonstration only. Passwords are
stored as ASP.NET Identity hashes.

## API endpoints

- `POST /api/auth/login`
- `GET /health`
- `GET /api/students`
- `GET /api/students/{id}`
- `POST /api/students`
- `PUT /api/students/{id}`
- `DELETE /api/students/{id}`
- `GET /api/courses`
- `GET /api/courses/{id}`
- `POST /api/courses`
- `PUT /api/courses/{id}`
- `DELETE /api/courses/{id}`
- `POST /api/courses/{courseId}/enrollments`
- `GET /api/courses/{courseId}/enrollments`
- `PUT /api/enrollments/{id}/mark`
- `DELETE /api/enrollments/{id}`
- `GET /api/courses/{courseId}/stats`
- `GET /api/students/{id}/transcript`

Teacher-only operations are protected by the Teacher role. Student users can
read only their own student record and transcript. List responses use paging
metadata where applicable, and duplicate database constraints are reported as
HTTP 409.

## Tests and smoke test

Run the database-backed authentication checks:

```bash
ConnectionStrings__Database="Host=127.0.0.1;Port=5432;Database=marks_db;Username=marks_user;Password=your_local_database_password" \
  dotnet run --project tests/MarksApi.Tests/MarksApi.Tests.csproj
```

Run the complete real HTTP smoke test against PostgreSQL:

```bash
dotnet run --project src/MarksApi/MarksApi.csproj -- --smoke-test
```

The smoke test is opt-in, reads credentials from configuration, exercises valid
and negative requests across the API, cleans its prefixed data, prints one
PASS or FAIL line per check, and exits nonzero on failure.

## Postman

Import `postman/MarksApi.postman_collection.json` and
`postman/Local.postman_environment.json`. Set the environment `baseUrl` to the
running API URL. The collection contains the folders Auth, Students, Courses,
Enrollments and Marks, Reports, Health, and Negative Tests. The login request
saves the bearer token automatically. Run the complete collection in order
against a fresh database state.

## Submission artifacts

- `db/schema.sql`: PostgreSQL schema-only export
- `docs/erd.png`: entity relationship diagram
- `docs/report.pdf`: short report with evidence
- `docs/screenshots/`: API, Postman, smoke test, and ERD evidence
- `postman/MarksApi.postman_collection.json`: Postman collection
- `postman/Local.postman_environment.json`: exported environment without a token
- `appsettings.Example.json`: fake configuration values only

## Project structure

```text
src/MarksApi/
  Data/
  Dtos/
  Entities/
  Migrations/
  Services/
  Program.cs
  SmokeTest.cs
tests/MarksApi.Tests/
postman/
db/schema.sql
docs/
```

Build output directories such as `bin` and `obj` are excluded by `.gitignore`.
