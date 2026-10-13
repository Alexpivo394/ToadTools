namespace SmartTags.Services;

/// <summary>
///     Все изменения запуска — внутри одной группы транзакций: в журнале отмены Revit это один шаг,
///     и весь запуск откатывается одним Ctrl+Z (или целиком при ошибке/отмене).
/// </summary>
internal sealed class TransactionRunner : IDisposable
{
    private readonly Document _document;
    private readonly Action<string>? _log;
    private readonly TransactionGroup _group;

    public TransactionRunner(Document document, string name, Action<string>? log)
    {
        _document = document;
        _log = log;
        _group = new TransactionGroup(document, name);
        _group.Start();
    }

    public void Run(string name, Action action)
    {
        using var transaction = new Transaction(_document, name);
        var silencer = new FailureSilencer(_log);
        transaction.Start();
        silencer.AttachTo(transaction);

        try
        {
            action();
        }
        catch
        {
            if (transaction.HasStarted()) transaction.RollBack();
            throw;
        }

        var status = transaction.Commit();
        if (status != TransactionStatus.Committed)
            _log?.Invoke($"Транзакция «{name}»: {status}");
    }

    /// <summary>
    ///     Временные изменения: <paramref name="change" /> применяется, затем <paramref name="read" />
    ///     читает результат, и всё откатывается.
    /// </summary>
    public void RunTemporary(string name, Action change, Action read)
    {
        using var group = new TransactionGroup(_document, name);
        group.Start();
        try
        {
            Run(name, change);
            read();
        }
        finally
        {
            group.RollBack();
        }
    }

    public void Commit()
    {
        if (_group.GetStatus() == TransactionStatus.Started) _group.Assimilate();
    }

    public void Dispose()
    {
        if (_group.GetStatus() == TransactionStatus.Started) _group.RollBack();
        _group.Dispose();
    }
}

/// <summary>Убирает предупреждения и разрешает ошибки, чтобы транзакция не останавливалась диалогами Revit.</summary>
internal sealed class FailureSilencer(Action<string>? log) : IFailuresPreprocessor
{
    public void AttachTo(Transaction transaction)
    {
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(this);
        options.SetClearAfterRollback(true);
        options.SetForcedModalHandling(false);
        transaction.SetFailureHandlingOptions(options);
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var unresolved = false;
        var resolved = false;
        foreach (var failure in accessor.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                accessor.DeleteWarning(failure);
                continue;
            }

            log?.Invoke($"Ошибка Revit ({failure.GetSeverity()}): {failure.GetDescriptionText()}");
            if (failure.HasResolutions())
            {
                accessor.ResolveFailure(failure);
                resolved = true;
            }
            else
            {
                unresolved = true;
            }
        }

        if (unresolved) return FailureProcessingResult.ProceedWithRollBack;
        return resolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
    }
}
