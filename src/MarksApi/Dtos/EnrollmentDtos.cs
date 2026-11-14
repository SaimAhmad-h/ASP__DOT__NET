namespace MarksApi.Dtos;

public record EnrollmentDto(int Id, int StudentId, int CourseId, int? Mark);
public record UpdateMarkDto(int Mark);