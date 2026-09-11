using Circle_Tracker.Services;
using FluentAssertions;
using Google;
using Google.Apis.Requests;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using Xunit;

namespace Circle_Tracker.Tests.ServiceTests;

public class SheetsFailureClassifierTests
{
    public static IEnumerable<object[]> ClassificationCases
    {
        get
        {
            yield return new object[] { HttpStatusCode.BadRequest, "badRequest", "Bad request", false, true };
            yield return new object[] { HttpStatusCode.Unauthorized, "authError", "Invalid credentials", false, true };
            yield return new object[] { HttpStatusCode.Forbidden, "forbidden", "Access denied", false, true };
            yield return new object[] { HttpStatusCode.NotFound, "notFound", "Requested entity was not found", false, true };
            yield return new object[] { HttpStatusCode.UnprocessableEntity, "failedPrecondition", "Precondition failed", false, true };
            yield return new object[] { (HttpStatusCode)429, "rateLimitExceeded", "Rate limit exceeded", true, false };
            yield return new object[] { HttpStatusCode.InternalServerError, "internalError", "Internal error", true, false };
            yield return new object[] { HttpStatusCode.BadGateway, "badGateway", "Bad gateway", true, false };
            yield return new object[] { HttpStatusCode.ServiceUnavailable, "backendError", "Backend error", true, false };
            yield return new object[] { HttpStatusCode.GatewayTimeout, "gatewayTimeout", "Gateway timeout", true, false };
            yield return new object[] { HttpStatusCode.Forbidden, "rateLimitExceeded", "Rate limit exceeded", true, false };
            yield return new object[] { HttpStatusCode.Forbidden, "quotaExceeded", "Quota exceeded", true, false };
            yield return new object[] { HttpStatusCode.Forbidden, "userRateLimitExceeded", "User rate limit exceeded", true, false };
            yield return new object[] { HttpStatusCode.Forbidden, "dailyLimitExceeded", "Daily limit exceeded", true, false };
        }
    }

    private static GoogleApiException BuildApiException(HttpStatusCode status, string reason, string message)
    {
        var error = new RequestError
        {
            Code = (int)status,
            Message = message,
            Errors = new List<SingleError>
            {
                new SingleError
                {
                    Reason = reason,
                    Message = message,
                },
            },
        };

        var ex = new GoogleApiException("sheets", message)
        {
            HttpStatusCode = status,
            Error = error,
        };

        return ex;
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classification_Theory_MapsStatusCodesCorrectly(HttpStatusCode status, string reason, string message, bool expectedTransient, bool expectedPermanent)
    {
        var ex = BuildApiException(status, reason, message);

        (SheetsFailureClassifier.IsTransient(ex), SheetsFailureClassifier.IsPermanent(ex)).Should().Be((expectedTransient, expectedPermanent));
    }

    [Fact]
    public void IsTransient_NetworkFailure_ReturnsTrue()
    {
        var ex = new HttpRequestException("Network unreachable");

        bool result = SheetsFailureClassifier.IsTransient(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPermanent_NetworkFailure_ReturnsFalse()
    {
        var ex = new IOException("Connection reset");

        bool result = SheetsFailureClassifier.IsPermanent(ex);

        result.Should().BeFalse();
    }
}
