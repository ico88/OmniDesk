using System.Text;
using OmniDesk.Api.Attachments;

namespace OmniDesk.Api.Tests;

public sealed class FakeAttachmentScanner : IAttachmentScanner
{
    public async Task<AttachmentScanVerdict> ScanAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var text = Encoding.ASCII.GetString(buffer.ToArray());
        return text.Contains("EICAR-STANDARD-ANTIVIRUS-TEST-FILE", StringComparison.Ordinal)
            ? AttachmentScanVerdict.Infected
            : AttachmentScanVerdict.Clean;
    }
}
