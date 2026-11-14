namespace MarksApi.Dtos;

public record CourseStatsDto(int CourseId, string CourseCode, int Enrolled, int Graded, double? Average, int? Highest, int? Lowest, int Passed);
public record TranscriptCourseDto(string Code, int CreditHours, int? Mark, bool Passed);
public record TranscriptDto(int StudentId, string RegistrationNo, string FullName, List<TranscriptCourseDto> Courses, double? WeightedAverage);
public record PagedResultDto<T>(List<T> Items, int Page, int PageSize, int TotalCount);