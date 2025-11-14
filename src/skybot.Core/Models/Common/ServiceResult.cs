namespace skybot.Core.Models.Common;

public class ServiceResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
    public int StatusCode { get; set; } = 200;

    public static ServiceResult Success(object? data = null) => new ServiceResult { IsSuccess = true, Data = data };
    public static ServiceResult BadRequest(string message) => new ServiceResult { IsSuccess = false, ErrorMessage = message, StatusCode = 400 };
    public static ServiceResult Ok(object? data = null) => new ServiceResult { IsSuccess = true, Data = data, StatusCode = 200 };
}

