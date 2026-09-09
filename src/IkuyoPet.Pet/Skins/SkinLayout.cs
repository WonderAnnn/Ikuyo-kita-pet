using System.Windows;

namespace IkuyoPet.Pet.Skins;

public static class SkinLayout
{
    public static Size CalculateUniformSize(int sourceWidth, int sourceHeight, double maxWidth, double maxHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (maxWidth <= 0 || maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        var scale = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);
        return new Size(sourceWidth * scale, sourceHeight * scale);
    }
}