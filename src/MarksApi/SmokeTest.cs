using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MarksApi;

public static class SmokeTestRunner
{
    public static async Task<bool> RunAsync(string baseUrl, IConfiguration configuration)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var teacher = configuration["SmokeTest:TeacherUsername"]
            ?? throw new InvalidOperationException("SmokeTest:TeacherUsername is required.");
        var teacherPassword = configuration["SmokeTest:TeacherPassword"]
            ?? throw new InvalidOperationException("SmokeTest:TeacherPassword is required.");
        var student = configuration["SmokeTest:StudentUsername"]
            ?? throw new InvalidOperationException("SmokeTest:StudentUsername is required.");
        var studentPassword = configuration["SmokeTest:StudentPassword"]
            ?? throw new InvalidOperationException("SmokeTest:StudentPassword is required.");

        var smokeId = Guid.NewGuid().ToString("N")[..8];
        int? studentId = null;
        int? courseId = null;
        int? enrollmentId = null;
        var passed = true;

        Console.WriteLine("=== STARTING SMOKE TEST ===");
        try
        {
            passed &= await Check(client, HttpMethod.Get, "/health", HttpStatusCode.OK);
            var teacherToken = await Login(client, teacher, teacherPassword);
            var studentToken = await Login(client, student, studentPassword);
            passed &= await Check(client, HttpMethod.Get, "/api/courses", HttpStatusCode.Unauthorized);

            using var teacherClient = AuthenticatedClient(baseUrl, teacherToken);
            using var studentClient = AuthenticatedClient(baseUrl, studentToken);

            passed &= await Check(teacherClient, HttpMethod.Get, "/api/students?page=1&pageSize=10&sort=name", HttpStatusCode.OK);
            passed &= await Check(teacherClient, HttpMethod.Get, "/api/students/999999", HttpStatusCode.NotFound);
            passed &= await Check(studentClient, HttpMethod.Get, "/api/students/2", HttpStatusCode.Forbidden);
            passed &= await Check(studentClient, HttpMethod.Get, "/api/students/1", HttpStatusCode.OK);
            passed &= await Check(teacherClient, HttpMethod.Get, "/api/courses", HttpStatusCode.OK);
            passed &= await Check(teacherClient, HttpMethod.Get, "/api/courses/1", HttpStatusCode.OK);
            passed &= await Check(teacherClient, HttpMethod.Get, "/api/courses/999999", HttpStatusCode.NotFound);

            var createdStudent = await teacherClient.PostAsJsonAsync(
                "/api/students",
                new { registrationNo = $"SMOKE-{smokeId}", fullName = "Smoke Student", email = $"smoke-{smokeId}@example.com" });
            passed &= Print("POST /api/students", HttpStatusCode.Created, createdStudent.StatusCode);
            studentId = await ReadId(createdStudent);

            var duplicateStudent = await teacherClient.PostAsJsonAsync(
                "/api/students",
                new { registrationNo = $"SMOKE-{smokeId}", fullName = "Smoke Student", email = $"smoke-{smokeId}@example.com" });
            passed &= Print("POST /api/students duplicate", HttpStatusCode.Conflict, duplicateStudent.StatusCode);

            var updatedStudent = await teacherClient.PutAsJsonAsync(
                $"/api/students/{studentId}", new { fullName = "Updated Smoke Student", email = $"updated-{smokeId}@example.com" });
            passed &= Print($"PUT /api/students/{studentId}", HttpStatusCode.OK, updatedStudent.StatusCode);

            var createdCourse = await teacherClient.PostAsJsonAsync(
                "/api/courses",
                new { code = $"SM{smokeId[..6]}", title = "Smoke Course", creditHours = 3 });
            passed &= Print("POST /api/courses", HttpStatusCode.Created, createdCourse.StatusCode);
            courseId = await ReadId(createdCourse);

            var updatedCourse = await teacherClient.PutAsJsonAsync(
                $"/api/courses/{courseId}", new { title = "Updated Smoke Course", creditHours = 4 });
            passed &= Print($"PUT /api/courses/{courseId}", HttpStatusCode.OK, updatedCourse.StatusCode);

            var invalidCourse = await teacherClient.PostAsJsonAsync(
                "/api/courses", new { code = $"BAD{smokeId[..5]}", title = "Invalid", creditHours = 5 });
            passed &= Print("POST /api/courses invalid", HttpStatusCode.BadRequest, invalidCourse.StatusCode);

            var enrollment = await teacherClient.PostAsync(
                $"/api/courses/{courseId}/enrollments?studentId={studentId}", null);
            passed &= Print($"POST /api/courses/{courseId}/enrollments", HttpStatusCode.Created, enrollment.StatusCode);
            enrollmentId = await ReadId(enrollment);

            var duplicateEnrollment = await teacherClient.PostAsync(
                $"/api/courses/{courseId}/enrollments?studentId={studentId}", null);
            passed &= Print("POST enrollment duplicate", HttpStatusCode.Conflict, duplicateEnrollment.StatusCode);

            passed &= await Check(teacherClient, HttpMethod.Get, $"/api/courses/{courseId}/enrollments", HttpStatusCode.OK);
            var mark = await teacherClient.PutAsJsonAsync($"/api/enrollments/{enrollmentId}/mark", new { mark = 88 });
            passed &= Print($"PUT /api/enrollments/{enrollmentId}/mark", HttpStatusCode.OK, mark.StatusCode);
            var invalidMark = await teacherClient.PutAsJsonAsync($"/api/enrollments/{enrollmentId}/mark", new { mark = 101 });
            passed &= Print("PUT mark invalid", HttpStatusCode.BadRequest, invalidMark.StatusCode);
            passed &= await Check(teacherClient, HttpMethod.Get, $"/api/courses/{courseId}/stats", HttpStatusCode.OK);
            passed &= await Check(teacherClient, HttpMethod.Get, $"/api/students/{studentId}/transcript", HttpStatusCode.OK);
            passed &= await Check(studentClient, HttpMethod.Get, "/api/students/1/transcript", HttpStatusCode.OK);
            passed &= await Check(studentClient, HttpMethod.Get, $"/api/students/{studentId}/transcript", HttpStatusCode.Forbidden);
            passed &= await Check(teacherClient, HttpMethod.Delete, $"/api/students/{studentId}", HttpStatusCode.Conflict);
            passed &= await Check(teacherClient, HttpMethod.Delete, $"/api/courses/{courseId}", HttpStatusCode.Conflict);
            passed &= await Check(teacherClient, HttpMethod.Delete, $"/api/enrollments/{enrollmentId}", HttpStatusCode.NoContent);
            enrollmentId = null;
            passed &= await Check(teacherClient, HttpMethod.Delete, $"/api/students/{studentId}", HttpStatusCode.NoContent);
            studentId = null;
            passed &= await Check(teacherClient, HttpMethod.Delete, $"/api/courses/{courseId}", HttpStatusCode.NoContent);
            courseId = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Unexpected smoke test error: {ex.Message}");
            passed = false;
        }
        finally
        {
            if (enrollmentId.HasValue)
                await TryDelete(client, $"/api/enrollments/{enrollmentId}");
            if (studentId.HasValue)
                await TryDelete(client, $"/api/students/{studentId}");
            if (courseId.HasValue)
                await TryDelete(client, $"/api/courses/{courseId}");
        }

        Console.WriteLine($"=== SMOKE TEST COMPLETE: {(passed ? "SUCCESS" : "FAILURE")} ===");
        return passed;
    }

    private static HttpClient AuthenticatedClient(string baseUrl, string token)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Print("POST /api/auth/login", HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static async Task<bool> Check(HttpClient client, HttpMethod method, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await client.SendAsync(request);
        return Print($"{method} {path}", expected, response.StatusCode);
    }

    private static async Task<int> ReadId(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task TryDelete(HttpClient client, string path)
    {
        try { await client.DeleteAsync(path); } catch { }
    }

    private static bool Print(string path, HttpStatusCode expected, HttpStatusCode actual)
    {
        var passed = expected == actual;
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {path} | Expected: {(int)expected} | Actual: {(int)actual}");
        return passed;
    }
}
