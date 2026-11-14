namespace MarksApi.Dtos;

public class ProblemDetailsDto
{
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Detail { get; set; }
    public IDictionary<string, string[]>? Errors { get; set; }
}