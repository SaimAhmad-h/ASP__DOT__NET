namespace MarksApi.Dtos;

public record StudentDto(int Id, string RegistrationNo, string FullName, string Email);
public record CreateStudentDto(string RegistrationNo, string FullName, string Email);
public record UpdateStudentDto(string FullName, string Email);