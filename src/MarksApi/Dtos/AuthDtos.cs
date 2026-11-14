namespace MarksApi.Dtos;

public record LoginRequestDto(string Username, string Password);
public record LoginResponseDto(string Token, DateTime ExpiresAtUtc, string Role);