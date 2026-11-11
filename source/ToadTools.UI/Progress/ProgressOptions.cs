#nullable enable
using ToadDesign;
using System;

namespace ToadTools.UI.Progress;

/// <summary>Настройки окна прогресса <see cref="ToadProgress" />.</summary>
public sealed class ProgressOptions
{
    /// <summary>Иконка в заголовке окна.</summary>
    public Symbol Icon { get; set; } = Symbol.Refresh;

    /// <summary>Показывать кнопку «Прервать».</summary>
    public bool CanCancel { get; set; } = true;

    /// <summary>Что произойдёт после нажатия «Прервать» — показывается под полосой.</summary>
    public string CancelHint { get; set; } = "Останавливаемся… изменения в модели будут откачены";

    /// <summary>Текст кнопки пропуска текущей задачи (см. <see cref="ToadProgress.BeginItem" />).</summary>
    public string SkipText { get; set; } = "Пропустить";

    /// <summary>
    ///     Окно появляется только если работа идёт дольше этого времени —
    ///     быстрые операции не мелькают лишним окном.
    /// </summary>
    public TimeSpan ShowDelay { get; set; } = TimeSpan.FromMilliseconds(400);
}
