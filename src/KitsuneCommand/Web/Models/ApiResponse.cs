namespace KitsuneCommand.Web.Models
{
    /// <summary>
    /// Standard API response envelope.
    /// </summary>
    public class ApiResponse
    {
        public int Code { get; set; }
        public string Message { get; set; }

        public static ApiResponse Ok(string message = "Success")
            => new ApiResponse { Code = 200, Message = message };

        public static ApiResponse Error(int code, string message)
            => new ApiResponse { Code = code, Message = message };

        public static ApiResponse<T> Ok<T>(T data, string message = "Success")
            => new ApiResponse<T> { Code = 200, Message = message, Data = data };
    }

    /// <summary>
    /// Standard API response envelope with data payload.
    /// </summary>
    public class ApiResponse<T> : ApiResponse
    {
        public T Data { get; set; }
    }

    /// <summary>
    /// Paginated response wrapper.
    /// </summary>
    public class PaginatedResponse<T>
    {
        public IEnumerable<T> Items { get; set; }
        public int Total { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
