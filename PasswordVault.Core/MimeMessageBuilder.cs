using System.Text;

namespace PasswordVault.Core;

public static class MimeMessageBuilder
{
    private const string NewLine = "\r\n";

    public static string Build(
        string from,
        string to,
        string subject,
        string body,
        string attachmentName,
        byte[] attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ValidateHeaderValue(from, nameof(from));
        ValidateHeaderValue(to, nameof(to));
        ValidateHeaderValue(subject, nameof(subject));
        ValidateHeaderValue(attachmentName, nameof(attachmentName));
        ArgumentNullException.ThrowIfNull(body);

        var boundary = $"----PasswordVault-{Guid.NewGuid():N}";
        var safeAttachmentName = EscapeQuotedString(attachmentName);
        var normalizedBody = NormalizeLineEndings(body);
        var message = new StringBuilder(attachment.Length * 2);
        message.Append("From: ").Append(from).Append(NewLine)
            .Append("To: ").Append(to).Append(NewLine)
            .Append("Subject: ").Append(EncodeSubject(subject)).Append(NewLine)
            .Append("MIME-Version: 1.0").Append(NewLine)
            .Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append('"').Append(NewLine)
            .Append(NewLine)
            .Append("--").Append(boundary).Append(NewLine)
            .Append("Content-Type: text/plain; charset=utf-8").Append(NewLine)
            .Append("Content-Transfer-Encoding: 8bit").Append(NewLine)
            .Append(NewLine)
            .Append(normalizedBody);
        if (!normalizedBody.EndsWith(NewLine, StringComparison.Ordinal))
        {
            message.Append(NewLine);
        }

        message.Append("--").Append(boundary).Append(NewLine)
            .Append("Content-Type: application/octet-stream; name=\"").Append(safeAttachmentName).Append('"').Append(NewLine)
            .Append("Content-Transfer-Encoding: base64").Append(NewLine)
            .Append("Content-Disposition: attachment; filename=\"").Append(safeAttachmentName).Append('"').Append(NewLine)
            .Append(NewLine);

        var base64 = Convert.ToBase64String(attachment);
        for (var offset = 0; offset < base64.Length; offset += 76)
        {
            message.Append(base64, offset, Math.Min(76, base64.Length - offset)).Append(NewLine);
        }

        if (base64.Length == 0)
        {
            message.Append(NewLine);
        }

        message.Append("--").Append(boundary).Append("--").Append(NewLine);
        return message.ToString();
    }

    public static string ToBase64Url(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string EncodeSubject(string subject)
    {
        return subject.Any(character => character > 0x7f)
            ? $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?="
            : subject;
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", NewLine, StringComparison.Ordinal);

    private static string EscapeQuotedString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void ValidateHeaderValue(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("Header values cannot contain line breaks.", parameterName);
        }
    }
}
