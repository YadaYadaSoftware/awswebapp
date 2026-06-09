namespace Tjb.Web.Services
{
    public class AwsSesOptions
    {
        public const string SectionName = "AwsSes";

        public string Region { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
    }
}
