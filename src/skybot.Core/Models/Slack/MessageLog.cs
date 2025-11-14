namespace skybot.Core.Models.Slack;

public record MessageLog(
    int Id,
    string TeamId,
    string MessageTs,
    string Channel,
    string DestinationType,
    string? ThreadTs,
    int? ApiKeyId,
    string? ApiKeyName,
    string? SourceIp,
    string? ForwardedFor,
    string? UserAgent,
    string? Referer,
    string? RequestId,
    string ContentType,
    bool HasAttachments,
    DateTime SentAt,
    string Status,
    DateTime? DeletedAt,
    string? ErrorMessage
);

public record CreateMessageLogRequest(
    string TeamId,
    string MessageTs,
    string Channel,
    DestinationType DestinationType,
    string? ThreadTs,
    int? ApiKeyId,
    string? ApiKeyName,
    string? SourceIp,
    string? ForwardedFor,
    string? UserAgent,
    string? Referer,
    string? RequestId,
    MessageContentType ContentType,
    bool HasAttachments
);

public enum MessageContentType
{
    TEXT,
    BLOCKS
}

public enum MessageLogStatus
{
    SENT,
    DELETED,
    FAILED
}

