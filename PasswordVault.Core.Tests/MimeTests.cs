using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class MimeTests
{
    [Fact]
    public void BuildCreatesMultipartMessageAndPreservesLargeAttachment()
    {
        var attachment = RandomNumberGenerator.GetBytes(100 * 1024);
        const string attachmentName = "vault-backup-20261116.pvlt";

        var message = MimeMessageBuilder.Build(
            "sender@example.com",
            "recipient@example.com",
            "Password vault backup",
            "Encrypted backup.",
            attachmentName,
            attachment);

        Assert.Contains("From: sender@example.com\r\n", message);
        Assert.Contains("To: recipient@example.com\r\n", message);
        Assert.Contains("MIME-Version: 1.0\r\n", message);
        Assert.Contains("Content-Type: multipart/mixed; boundary=\"", message);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", message);
        Assert.Contains("\r\n\r\nEncrypted backup.\r\n", message);
        Assert.Contains("Content-Type: application/octet-stream;", message);
        Assert.Contains($"Content-Disposition: attachment; filename=\"{attachmentName}\"", message);

        var boundary = Regex.Match(message, "boundary=\"([^\"]+)\"").Groups[1].Value;
        var attachmentHeader = $"Content-Disposition: attachment; filename=\"{attachmentName}\"";
        var attachmentStart = message.IndexOf(attachmentHeader, StringComparison.Ordinal);
        Assert.True(attachmentStart >= 0);
        var encodedStart = message.IndexOf("\r\n\r\n", attachmentStart, StringComparison.Ordinal) + 4;
        var encodedEnd = message.IndexOf($"\r\n--{boundary}", encodedStart, StringComparison.Ordinal);
        Assert.True(encodedEnd > encodedStart);
        var encodedLines = message[encodedStart..encodedEnd].TrimEnd('\r', '\n').Split("\r\n");
        Assert.All(encodedLines, line => Assert.InRange(line.Length, 1, 76));
        Assert.All(encodedLines[..^1], line => Assert.Equal(76, line.Length));
        var decodedAttachment = Convert.FromBase64String(string.Concat(encodedLines));

        Assert.Equal(attachment, decodedAttachment);
        Assert.EndsWith($"--{boundary}--\r\n", message);
    }

    [Fact]
    public void BuildEncodesNonAsciiSubjectAsUtf8Base64EncodedWord()
    {
        const string subject = "Résumé 🔐";

        var message = MimeMessageBuilder.Build(
            "sender@example.com",
            "recipient@example.com",
            subject,
            "Encrypted backup.",
            "backup.pvlt",
            [1, 2, 3]);

        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        Assert.Contains($"Subject: =?UTF-8?B?{encodedSubject}?=\r\n", message);
    }

    [Fact]
    public void ToBase64UrlEncodesUtf8WithoutPadding()
    {
        const string message = "MIME message: كلمة 🔐";

        var encoded = MimeMessageBuilder.ToBase64Url(message);
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');

        Assert.DoesNotContain('=', encoded);
        Assert.Equal(message, Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
    }
}
