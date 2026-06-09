namespace Sample.Shared;

/// <summary>
/// DTO for a guestbook entry — the sample's own shared contract (Q2: the sample carries
/// a dedicated Sample.Shared rather than consuming Tjb.Shared).
/// </summary>
public record GuestbookEntryDto(int Id, string AuthorUserId, string Message, DateTime CreatedUtc);
