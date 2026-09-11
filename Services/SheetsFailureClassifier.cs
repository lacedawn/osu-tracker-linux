using Google;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public static class SheetsFailureClassifier
{
    public static bool IsTransient(Exception ex)
    {
        if (ex is GoogleApiException apiEx)
        {
            return IsTransientStatusCode(apiEx);
        }

        return ex is HttpRequestException
            || ex is IOException
            || ex is TimeoutException
            || ex is TaskCanceledException;
    }

    public static bool IsPermanent(Exception ex)
    {
        if (ex is GoogleApiException apiEx)
        {
            return IsPermanentStatusCode(apiEx);
        }

        return false;
    }

    private static bool IsTransientStatusCode(GoogleApiException apiEx)
    {
        int statusCode = (int)apiEx.HttpStatusCode;

        if (statusCode == 429)
        {
            return true;
        }

        if (statusCode >= 500 && statusCode <= 599)
        {
            return true;
        }

        if (statusCode == 403)
        {
            return IsQuotaRateLimitSignal(apiEx);
        }

        return false;
    }

    private static bool IsPermanentStatusCode(GoogleApiException apiEx)
    {
        int statusCode = (int)apiEx.HttpStatusCode;

        if (statusCode == 429)
        {
            return false;
        }

        if (statusCode >= 500 && statusCode <= 599)
        {
            return false;
        }

        if (statusCode == 403)
        {
            return !IsQuotaRateLimitSignal(apiEx);
        }

        return statusCode >= 400 && statusCode <= 499;
    }

    private static bool IsQuotaRateLimitSignal(GoogleApiException apiEx)
    {
        string reason = string.Concat(
            string.Join(" ", CollectReasons(apiEx)),
            " ",
            apiEx.Error?.Message ?? string.Empty,
            " ",
            apiEx.Message ?? string.Empty);

        string lowered = reason.ToLowerInvariant();

        return lowered.Contains("ratelimitexceeded")
            || lowered.Contains("userratelimitexceeded")
            || lowered.Contains("quotaexceeded")
            || lowered.Contains("dailylimitexceeded")
            || lowered.Contains("limitexceeded")
            || lowered.Contains("quota")
            || lowered.Contains("rate limit")
            || lowered.Contains("rate_limit")
            || lowered.Contains("ratelimit")
            || lowered.Contains("too many")
            || lowered.Contains("slow down");
    }

    private static IEnumerable<string> CollectReasons(GoogleApiException apiEx)
    {
        var errors = apiEx.Error?.Errors;
        if (errors == null)
        {
            yield break;
        }

        foreach (var single in errors)
        {
            if (!string.IsNullOrEmpty(single?.Reason))
            {
                yield return single.Reason;
            }

            if (!string.IsNullOrEmpty(single?.Message))
            {
                yield return single.Message;
            }
        }
    }
}
