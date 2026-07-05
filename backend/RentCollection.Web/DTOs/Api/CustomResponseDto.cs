namespace RentCollection.Web.DTOs.Api;

public class CustomResponseDto<T>
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
    public List<string>? Message { get; set; }

    public static CustomResponseDto<T> SuccessResponse(T data, string message = "Success")
    {
        return new CustomResponseDto<T>
        {
            Success = true,
            StatusCode = 200,
            Data = data,
            Message = new List<string> { message },
            Errors = null
        };
    }

    public static CustomResponseDto<T> ErrorResponse(string error, int statusCode = 400)
    {
        return new CustomResponseDto<T>
        {
            Success = false,
            StatusCode = statusCode,
            Data = default,
            Errors = new List<string> { error },
            Message = null
        };
    }

    public static CustomResponseDto<T> ErrorResponse(List<string> errors, int statusCode = 400)
    {
        return new CustomResponseDto<T>
        {
            Success = false,
            StatusCode = statusCode,
            Data = default,
            Errors = errors,
            Message = null
        };
    }
}
