using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;

namespace MagicLinkDemo.Services;

public class EmailSender
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly string _fromAddress;

    public EmailSender(IAmazonSimpleEmailServiceV2 sesClient, IConfiguration configuration)
    {
        _sesClient = sesClient;
        _fromAddress = configuration["SES_FROM_ADDRESS"] ?? 
                       Environment.GetEnvironmentVariable("SES_FROM_ADDRESS") ?? 
                       throw new InvalidOperationException("SES_FROM_ADDRESS environment variable is required");
    }

    public async Task SendMagicLinkAsync(string toEmail, string magicLink)
    {
        var subject = "Your Magic Link - MagicLinkDemo";
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; text-align: center; padding: 20px; border-radius: 5px; }}
        .content {{ background-color: #f9f9f9; padding: 20px; margin: 20px 0; border-radius: 5px; }}
        .button {{ display: inline-block; background-color: #4CAF50; color: white; text-decoration: none; padding: 12px 24px; border-radius: 5px; margin: 10px 0; }}
        .footer {{ text-align: center; color: #666; font-size: 12px; margin-top: 20px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🔗 Magic Link Login</h1>
        </div>
        <div class=""content"">
            <h2>Hello!</h2>
            <p>You requested a magic link to sign in to MagicLinkDemo.</p>
            <p>Click the button below to complete your login:</p>
            <p style=""text-align: center;"">
                <a href=""{magicLink}"" class=""button"">Sign In Now</a>
            </p>
            <p><strong>This link will expire in 15 minutes</strong> and can only be used once.</p>
            <p>If you didn't request this link, you can safely ignore this email.</p>
        </div>
        <div class=""footer"">
            <p>This is an automated message from MagicLinkDemo</p>
        </div>
    </div>
</body>
</html>";

        var textBody = $@"
Magic Link Login

Hello!

You requested a magic link to sign in to MagicLinkDemo.

Copy and paste this link into your browser to complete your login:
{magicLink}

This link will expire in 15 minutes and can only be used once.

If you didn't request this link, you can safely ignore this email.

---
This is an automated message from MagicLinkDemo
";

        var request = new SendEmailRequest
        {
            FromEmailAddress = _fromAddress,
            Destination = new Destination
            {
                ToAddresses = new List<string> { toEmail }
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body
                    {
                        Html = new Content { Data = htmlBody },
                        Text = new Content { Data = textBody }
                    }
                }
            }
        };

        await _sesClient.SendEmailAsync(request);
    }
}
