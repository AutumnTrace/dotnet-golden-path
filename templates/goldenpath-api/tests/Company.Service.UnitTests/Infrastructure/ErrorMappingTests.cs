using Company.Service.Api.Infrastructure.Errors;

namespace Company.Service.UnitTests.Infrastructure;

public class ErrorMappingTests
{
    [Theory]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Unprocessable, 422)]
    [InlineData(ErrorKind.Unavailable, 503)]
    public void Every_error_kind_maps_to_one_status_and_keeps_its_code(ErrorKind kind, int status)
    {
        var problem = new Error("things.broken", "Something is broken.", kind).ToProblem();

        Assert.Equal(status, problem.StatusCode);
        Assert.Equal("Something is broken.", problem.ProblemDetails.Detail);
        Assert.Equal("things.broken", problem.ProblemDetails.Extensions[ProblemDetailsExtensions.ErrorCodeKey]);
    }

    [Fact]
    public void A_failed_result_refuses_to_hand_out_a_value()
    {
        Result<int> result = Error.NotFound("things.missing", "Gone.");

        Assert.False(result.IsSuccess);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("things.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_successful_result_has_a_value_and_no_error()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(42, result.Value);
    }
}
