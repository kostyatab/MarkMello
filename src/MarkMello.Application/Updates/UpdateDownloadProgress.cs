namespace MarkMello.Application.Updates;

/// <summary>
/// Ход загрузки обновления: сколько байт уже получено и сколько всего, если сервер
/// назвал размер. Без размера доля неизвестна — кольцо и полоса просто крутятся.
/// </summary>
public readonly record struct UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Доля от 0 до 1 или <c>null</c>, если размер неизвестен.</summary>
    public double? Fraction => TotalBytes is > 0 and var total
        ? Math.Clamp((double)BytesReceived / total, 0, 1)
        : null;
}
