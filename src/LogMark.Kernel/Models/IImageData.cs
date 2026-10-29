namespace LogMark.Models;

/// <summary>
/// Platform-agnostic interface for image data.
/// Allows Core to work with images without depending on System.Drawing.
/// </summary>
public interface IImageData : IDisposable
{
    /// <summary>
    /// Raw image data as bytes (PNG encoded).
    /// </summary>
    byte[] Data { get; }

    /// <summary>
    /// Image format (e.g., "png", "jpeg").
    /// </summary>
    string Format { get; }

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    int Height { get; }
}
