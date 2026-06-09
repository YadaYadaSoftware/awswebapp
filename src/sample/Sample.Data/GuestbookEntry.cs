namespace Sample.Data;

/// <summary>
/// A single guestbook post, tied to the signed-in Identity user (IdentityUser.Id is a string).
/// </summary>
public class GuestbookEntry
{
    public int Id { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
