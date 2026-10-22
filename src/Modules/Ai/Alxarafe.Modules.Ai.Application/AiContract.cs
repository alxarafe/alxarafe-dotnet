namespace Alxarafe.Modules.Ai.Application;

public static class AiContract
{
    public const string ModuleId = "erbas.ai";
    public const string AssistantPermission = "ai.assistant.invoke";
    public const int MessageMaximum = 4000;
    public const string InvalidRequest = "invalid_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string ModuleDisabled = "module_disabled";
    public const string AssistantUnavailable = "assistant_unavailable";
    public const string AssistantExecutionFailed = "assistant_execution_failed";
}
