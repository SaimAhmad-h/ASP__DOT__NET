namespace MarksApi.Dtos;

public record CourseDto(int Id, string Code, string Title, int CreditHours);
public record CreateCourseDto(string Code, string Title, int CreditHours);
public record UpdateCourseDto(string Title, int CreditHours);