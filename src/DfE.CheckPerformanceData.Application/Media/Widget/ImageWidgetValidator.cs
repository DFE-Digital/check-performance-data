using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// Checks the Image widget's props when an editor saves the widget. The image must be in the
/// library. Alt text is required unless the image is decorative; a decorative image saves an empty
/// alt text, so a screen reader skips it. Width, alignment and aspect ratio come from fixed lists.
/// The link must be safe (<see cref="SafeLink"/>). The crop and focal point are fractions of the
/// image. Errors come back in the order the fields appear in the editor.
/// </summary>
public sealed class ImageWidgetValidator(IMediaAssetRepository assets) : IWidgetPropsValidator
{
    public string WidgetType => ImageWidgetProps.Type;

    public async Task<WidgetPropsValidation> ValidateAsync(JsonObject props, CancellationToken ct = default)
    {
        var errors = new List<WidgetFieldError>();
        var saved = props.DeepClone().AsObject();

        if (!ImageWidgetProps.TryMediaId(props, out var mediaId))
            errors.Add(new(ImageWidgetProps.MediaId, "Select an image"));
        else if (await assets.GetAsync(mediaId, ct) is null)
            errors.Add(new(ImageWidgetProps.MediaId, "Select an image that is in the media library"));

        var decorative = ImageWidgetProps.IsDecorative(props);
        saved[ImageWidgetProps.Decorative] = decorative ? "true" : "false";
        if (decorative)
            saved[ImageWidgetProps.Alt] = "";
        else if (string.IsNullOrWhiteSpace(ImageWidgetProps.Text(props, ImageWidgetProps.Alt)))
            errors.Add(new(ImageWidgetProps.Alt, "Enter alt text, or select that the image is decorative"));

        if (!ImageWidgetProps.Widths.Contains(ImageWidgetProps.Text(props, ImageWidgetProps.Width), StringComparer.Ordinal))
            errors.Add(new(ImageWidgetProps.Width, "Select a width"));

        if (!ImageWidgetProps.Aligns.Contains(ImageWidgetProps.Text(props, ImageWidgetProps.Align), StringComparer.Ordinal))
            errors.Add(new(ImageWidgetProps.Align, "Select an alignment"));

        var href = ImageWidgetProps.Text(props, ImageWidgetProps.Href);
        if (href.Length > 0 && !SafeLink.IsSafe(href))
            errors.Add(new(ImageWidgetProps.Href, "Enter a link that starts with https://, http:// or /"));

        var crop = ImageWidgetProps.Text(props, ImageWidgetProps.Crop);
        if (crop.Length > 0 && ImageWidgetProps.ParseCrop(crop) is null)
            errors.Add(new(ImageWidgetProps.Crop, "Enter the crop as x,y,width,height: 4 numbers from 0 to 1 that stay inside the image"));

        if (!ImageWidgetProps.Aspects.Contains(ImageWidgetProps.Text(props, ImageWidgetProps.Aspect), StringComparer.Ordinal))
            errors.Add(new(ImageWidgetProps.Aspect, "Select an aspect ratio"));

        var focal = ImageWidgetProps.Text(props, ImageWidgetProps.Focal);
        if (focal.Length > 0 && ImageWidgetProps.ParseFocal(focal) is null)
            errors.Add(new(ImageWidgetProps.Focal, "Enter the focal point as x,y: 2 numbers from 0 to 1"));

        return new WidgetPropsValidation(saved, errors);
    }
}
