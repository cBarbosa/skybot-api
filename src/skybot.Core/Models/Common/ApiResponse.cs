namespace skybot.Core.Models.Common;

public record ApiResponse<T>(
    bool Success,
    T? Data = default,
    string? Message = null,
    string? Error = null
) where T : class;

