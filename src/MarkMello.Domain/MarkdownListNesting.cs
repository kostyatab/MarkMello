namespace MarkMello.Domain;

/// <summary>
/// Сколько списков каждого вида открыто вокруг текущего места документа. Маркер
/// пункта зависит от уровня списка, а уровень считается только среди предков
/// того же вида, как <c>ul ul</c> и <c>ol ol</c> в CSS: маркированный список
/// внутри нумерованного — всё ещё первый маркированный.
/// </summary>
/// <remarks>
/// Все, кто пишет маркеры, — экран, текстовая карта, копия для Telegram — обходят
/// документ сами и несут это значение вниз по дереву, заходя в список через
/// <see cref="Enter"/>: так маркеры везде совпадают.
/// </remarks>
public readonly record struct MarkdownListNesting(int BulletDepth, int OrderedDepth)
{
    /// <summary>Уровень списка с нуля: сколько списков того же вида вокруг него.</summary>
    public int LevelOf(MarkdownListBlock list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.IsOrdered ? OrderedDepth : BulletDepth;
    }

    /// <summary>Вложенность внутри пунктов списка <paramref name="list"/>.</summary>
    public MarkdownListNesting Enter(MarkdownListBlock list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.IsOrdered
            ? this with { OrderedDepth = OrderedDepth + 1 }
            : this with { BulletDepth = BulletDepth + 1 };
    }
}
