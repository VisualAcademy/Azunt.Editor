using System.Globalization;
using System.Text;

namespace Azunt.Web.Components.Pages;

// Synthetic fixtures only. Never use real application records in a public demo.
internal static class ExportDemoSamples
{
    private const string Logo = "iVBORw0KGgoAAAANSUhEUgAAALQAAAAwCAIAAAA3jserAAAE10lEQVR42u2cf1DTdRjHn++272QElMV5/tFV5HDcgDXYADcYR1GcdomkBlc2PSKh00vpSLrOfliXZwd3qOgtwSSRCoO8ELkuvFzhwA2FCcgOFYrAXwn5R0AHsn23/vhwu8lkwtgm2573cdx4vp8fu31eez/P870xymKxAAp1P7HwJUAhHCiEA+U6cRxfDldWu3zL3qrX8XX3ClEzFaTuwAIR8YW04m4yPLMFyvVweOzYkA8vg8PDB4Z8YLeC8n44Hsr7GM0DnQPlf3DYdqTYnSIc08mw/41COAA9A+GYGxAICsKBRuL74rjk+MOV1e4Goqbul11FB882VIU+vhgA9uwr7+7pBQCz2dzRfaVH2wAAsc+v0/92goy3Po5KTBdHR3x7qMg2fvr3c5XVPwFAe4dBIo4EAGVm+spUhXW76KR0UaSAoiij0ZT16qq1r7xkDZIBqcmytzasjUpMfyE5oXTPThIs+LioUd3c3VIPACdOna6qqadpjtFo2pi1hqzgF3Dc93bFtM7FtTcw1BrdpqyMppYL61anAcCH+bkkXnm8TiKOcjCRy6UZhmlt70qQiKzBtBR5WoqcsPJdWbH9LJqmSXx8fCKvYBcvIGDViwpr0Hbx/oHrjNnMZrEsFsvgjZtcLg0AGm177cnGY6ovQ4KDRkbHct/7dOmSUHl8jO+nFXvbcPezHJ+4Oz4+kZmxUq1ptY0PDd/5sb5xa84bjqdvy1XuL6tybmseL6Dw3ZzK43UzDRAK+JcMVwGg5+qfEfxnSfDrqtoPtr8dEhwEACHBQYXbcsoqa3y/5nCQPqZR4sJEo9G1K+TSsKefvHHrttFossZ3l5QVbMnmBSxyPF0WJwYAXVunc7tHhIcNXLs501WFTKrRtQGARtumkElIsO+va0LBMuuYyAh+X/+g3xWkjm3DVXycadKe/Fm9Pjt/aPjOeX3XFDHadsZsTkmKn80K2/OU+8udNA8TY+Zw2ABgNBo35O0gPxcv9UzBsSL23PmLAKBt65wpcVgsQPl8QfrAhOKOypQxm/sHrp/6XkWAUDe3JibE3p2cLD5YUb73s3tIZ1Ek/TMMw2azbS8lSERsFkt7ocOJJ9BluCLgh9kWIrZ6NCSYoli3bg8DQNAjgSTID3vKcLkvViQkfxou94YvewZbWdcnF32nIWL5VC6XxkQ16/QAoKqozng5demSUNuR0UJBS6ueMCQSLrc3j9K5m8e/I2NFB45s3viagzHJcmmJ6miijW1sVq4vKj0yOvYfAIyMjhUfqHC8gtc7h9N16Dw7l1+btDKpeKo8DFj0xOLH/ugfPHys9rlIwZmzWhI/vPfzwEDeJzu27Pxi36FvfgCA3R/lT1snLiaapunJSeNsNiUZhKIok4nJ3ZRJOh0SJANiRML3t2aTxymJ8SWqow3VX1mnJ62Q/D30z5vvFHK5NGll5XFir4Pjns+QOjhCewOYU7XhmdIEtSDSygOdwH2dC2phweH5Gxso3yxI0Tz8CA4X2gbygc6BCcg/4Ji/bWBy8V7N6n9lXXWiaCQ+mFbIoTp9tPOcjlroNcc8jxbJwIIUhXCgEA4UwvGQ+0xsbtE5UN4Ph4ffx2gbXuYcHjswJMMr04oHjg3JWPiiHH+9NX7VJMKBQmG3gkI4UAgHyr36H99E8nDr9KUeAAAAAElFTkSuQmCC";
    private const string Signature = "iVBORw0KGgoAAAANSUhEUgAAALQAAAAwCAIAAAA3jserAAAB7klEQVR42u2cS47DIBBExxZXyaG8yOm8yKF8GM8CyUL+YGgam8+rRTSKMoRUF9UFRBnWdf0D4AwjFADEARAHQBwAcQDEARAHqAtGZZTP92f/WOYJTnGOE2UAxHEO6xkIBXFgG4gj0jZAYxhSLt6OOdQ+E66VWpJsn4l7fJGvWlpSt60zta2kr6SSk+zn+3Mn1lv3HN9aTLENiFZSmXMcWWtjQ7sZxjJPPWftEdtQT9n9iiOH2RbiNxiGTlvxcHdbaZWFaAdR1BMJ4wgjY7AE598eVXR2JYtue4rQOVKYuuI6cEx3b+lmArFqMQwdcYQXQJwh/P/iFtK+hRsOYt/u4YRR4w7OlGAbiUt8mSe30YSM/5hhuJpI18fD9maqEP5tLV2v8qeQKFmI1ZzpXFWlCuHzCbp4k62zHbMhRB9fE8uy//WxH0R8j1jyKU74xEwOxb24vbyykKx9pKL7l6jpmahP/mTaSCnnLoXIqLmdc/N3cqZM20hf6LsdU6bG3/YG+CZzJJIr4FGdevEp2Wlm6upQxBR1QJTv4kasjJ6/z2EykSsbxBajnBr0fHbuE4fWljp2nHIqwYH6mJWger/1gzIunUOrqFDcoHNs11oQ1DMGfjAOCDMHQBwAIA6AOADiAIgDIA6AOADiAIgDNIx/MkFWcpVfOnYAAAAASUVORK5CYII=";

    public static string SignatureDataUrl => $"data:image/png;base64,{Signature}";

    public const string Document = "<h1>Sample document</h1><p>Normal body text and <strong>bold text</strong>.</p>" +
        "<p><span style='font-size:20px;color:#245ca6'>Explicit 20px text</span> remains explicitly formatted.</p>" +
        "<ul><li>First item</li><li>Second item</li></ul><p>Document end.</p>";

    public static string Images => $"<p style='text-align:center'><img src='data:image/png;base64,{Logo}' width='180' alt='Demo logo'></p>" +
        "<h2>Image and signature sample</h2><table><tr><th colspan='2'>Approval</th></tr>" +
        $"<tr><td>Example reviewer</td><td><img src='data:image/png;base64,{Signature}' width='180' alt='Fictional signature'></td></tr></table>" +
        "<p>Illustrative signature, not a real person's signature.</p>";

    public static string Report(int rows)
    {
        var html = new StringBuilder($"<p style='text-align:center'><img src='data:image/png;base64,{Logo}' width='180' alt='Demo logo'></p>");
        html.Append("<h2 style='text-align:center'>Sample operations report</h2><p>Fictional data for document export testing.</p>");
        // Deliberately use an all-th first row without thead to exercise header promotion.
        html.Append("<table><tr><th style='width:17%'>Report #</th><th style='width:20%'>Department</th><th style='width:20%'>Date</th><th style='width:12%'>Quantity</th><th>Summary</th></tr>");
        for (var i = 1; i <= rows; i++)
        {
            var date = new DateTime(2026, 1, 1).AddDays(i - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>R-{i:D6}</td><td>{(i % 2 == 0 ? "Operations" : "Accounting")}</td><td>{date}</td><td>{i}</td><td>Sample record {i:D3}</td></tr>");
        }
        html.Append("</table><p>End of report.</p><table><tr><td colspan='2'>Reviewed by</td></tr><tr><td>Authority</td><td>Name and date</td></tr></table>");
        return html.ToString();
    }
}
