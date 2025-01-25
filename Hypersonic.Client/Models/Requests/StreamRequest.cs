using System.Text;
using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class StreamRequest : StringIdRequest
{
    public int? MaxBitrate { get; set; }

    public string? Format { get; set; }

    public int? TimeOffset { get; set; }

    public string? Size { get; set; }

    public bool EstimateContentLength { get; set; }

    public bool Converted { get; set; }

    public override string Action => Constants.Action.Stream;

    protected override string IdName => StringIdRequest.IdNameId;

    public StreamRequest(SubsonicRequest subsonicRequest, string id, int? maxBitrate, string? format, int? timeOffset, string? size, bool estimateContentLength, bool Converted)
        : base(subsonicRequest, id)
    {
        this.MaxBitrate = maxBitrate;
        this.Format = format;
        this.TimeOffset = timeOffset;
        this.Size = size;
        this.EstimateContentLength = estimateContentLength;
        this.Converted = Converted;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        if (this.MaxBitrate.HasValue)
        {
            sb.Append($"&maxBitRate={this.MaxBitrate}");
        }

        if (!string.IsNullOrEmpty(this.Format))
        {
            sb.Append($"&format={this.Format}");
        }

        if (this.TimeOffset.HasValue)
        {
            sb.Append($"&timeOffset={this.TimeOffset}");
        }

        if (!string.IsNullOrEmpty(this.Size))
        {
            sb.Append($"&size={this.Size}");
        }

        if (this.EstimateContentLength)
        {
            sb.Append($"&estimateContentLength=true");
        }

        if (this.Converted)
        {
            sb.Append($"&converted=true");
        }

        return sb.ToString();
    }
}
