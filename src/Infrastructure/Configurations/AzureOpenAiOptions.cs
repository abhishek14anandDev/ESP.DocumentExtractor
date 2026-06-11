namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string DeploymentName { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = "2024-10-21";
    public int MaxTokens { get; init; } = 1500;
    public decimal Temperature { get; init; } = 0;
}
