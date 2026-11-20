using Loans.Core.Common;

namespace Loans.Core.Tests.Common;

public class ResultTests
{
    private static readonly Error TestError = new("test.error", "Something went wrong.");

    [Fact]
    public void Success_IsSuccessWithNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_IsFailureWithError()
    {
        var result = Result.Failure(TestError);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(TestError, result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericFailure_ExposesError()
    {
        var result = Result<int>.Failure(TestError);

        Assert.True(result.IsFailure);
        Assert.Equal(TestError, result.Error);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result<int>.Failure(TestError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(2).Map(x => x * 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value);
    }

    [Fact]
    public void Map_OnFailure_ShortCircuits()
    {
        var invoked = false;

        var result = Result<int>.Failure(TestError).Map(x =>
        {
            invoked = true;
            return x * 10;
        });

        Assert.False(invoked);
        Assert.True(result.IsFailure);
        Assert.Equal(TestError, result.Error);
    }

    [Fact]
    public void Bind_OnSuccess_ChainsResult()
    {
        var result = Result<int>.Success(2).Bind(x => Result<string>.Success($"v{x}"));

        Assert.True(result.IsSuccess);
        Assert.Equal("v2", result.Value);
    }

    [Fact]
    public void Bind_OnSuccess_PropagatesInnerFailure()
    {
        var result = Result<int>.Success(2).Bind(_ => Result<string>.Failure(TestError));

        Assert.True(result.IsFailure);
        Assert.Equal(TestError, result.Error);
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuits()
    {
        var invoked = false;

        var result = Result<int>.Failure(TestError).Bind(x =>
        {
            invoked = true;
            return Result<string>.Success($"v{x}");
        });

        Assert.False(invoked);
        Assert.True(result.IsFailure);
        Assert.Equal(TestError, result.Error);
    }
}
