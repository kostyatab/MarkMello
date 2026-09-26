namespace MarkMello.Domain.Outline;

/// <summary>
/// Раскладка чёрточек рельса по высоте: шаг между соседними и какие пункты видны.
/// Сначала шаг сжимается до минимума; если и так не помещаются — рельс показывает
/// окно вокруг текущего пункта, и окно едет вместе с чтением.
/// </summary>
/// <param name="Pitch">Расстояние между верхами соседних чёрточек.</param>
/// <param name="FirstVisibleIndex">Индекс первого видимого пункта.</param>
/// <param name="VisibleCount">Сколько пунктов видно подряд начиная с первого.</param>
public readonly record struct DocumentOutlineRailLayout(double Pitch, int FirstVisibleIndex, int VisibleCount)
{
    public bool IsWindowed(int entryCount) => VisibleCount < entryCount;

    /// <param name="entryCount">Число пунктов оглавления.</param>
    /// <param name="currentIndex">Текущий пункт; вокруг него строится окно.</param>
    /// <param name="availableHeight">Высота, доступная под чёрточки (от верха первой до верха последней).</param>
    /// <param name="preferredPitch">Шаг, когда места хватает.</param>
    /// <param name="minimumPitch">Шаг, меньше которого чёрточки сливаются.</param>
    public static DocumentOutlineRailLayout Compute(
        int entryCount,
        int currentIndex,
        double availableHeight,
        double preferredPitch,
        double minimumPitch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumPitch);
        ArgumentOutOfRangeException.ThrowIfLessThan(preferredPitch, minimumPitch);

        if (entryCount <= 1)
        {
            return new DocumentOutlineRailLayout(preferredPitch, 0, entryCount);
        }

        var available = Math.Max(0, availableHeight);
        var gaps = entryCount - 1;
        if (gaps * preferredPitch <= available)
        {
            return new DocumentOutlineRailLayout(preferredPitch, 0, entryCount);
        }

        if (gaps * minimumPitch <= available)
        {
            return new DocumentOutlineRailLayout(available / gaps, 0, entryCount);
        }

        var visible = Math.Max(1, (int)Math.Floor(available / minimumPitch) + 1);
        var current = Math.Clamp(currentIndex, 0, entryCount - 1);
        var first = Math.Clamp(current - visible / 2, 0, entryCount - visible);
        return new DocumentOutlineRailLayout(minimumPitch, first, visible);
    }
}
