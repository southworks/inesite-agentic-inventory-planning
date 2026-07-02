using Azure;

namespace GrokInventoryAndTrend.AgentProvisioning;

internal static class AzureServiceExceptionDetails
{
    public static string Describe(Exception exception)
    {
        if (exception is RequestFailedException requestFailed)
        {
            return $"Status={requestFailed.Status}, ErrorCode={requestFailed.ErrorCode}, Message={requestFailed.Message}";
        }

        return exception.Message;
    }
}
