using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Installer.UI.Views;
using ToadDesign;
using WixToolset.Dtf.WindowsInstaller;

namespace Installer.UI;

/// <summary>
///     The ToadTools installer window inside msiexec. MSI calls <see cref="Initialize" /> before the installation,
///     <see cref="ProcessMessage" /> for every message while it runs and <see cref="Shutdown" /> at the end.
///     The window runs on its own STA thread; the MSI thread waits for the user's choice in Initialize, then only
///     posts parsed messages to the window.
/// </summary>
public sealed class ToadToolsEmbeddedUI : IEmbeddedUI
{
    private readonly ManualResetEvent _started = new(false);
    private readonly ManualResetEvent _closed = new(false);
    private readonly ProgressCounter _counter = new();
    private Thread? _thread;
    private SetupViewModel? _model;
    private Dispatcher? _dispatcher;
    private SetupChoice? _choice;

    public bool Initialize(Session session, string resourcePath, ref InstallUIOptions internalUILevel)
    {
        // Reduced and quiet installations keep the standard behaviour: nothing to show, nothing to ask.
        if ((internalUILevel & InstallUIOptions.Full) != InstallUIOptions.Full) return false;

        Trace.Write("Initialize, UI level " + internalUILevel);
        SetupInfo info;
        try
        {
            info = SetupInfo.Read(session);
        }
        catch (Exception exception)
        {
            Trace.Write("Reading the session failed, the standard dialogs take over: " + exception);
            return false;
        }

        Trace.Write($"{info.Mode} {info.Version}, previous '{info.PreviousVersion}', per machine {info.PerMachine}, " +
                    string.Join(", ", info.Targets.Select(t => $"{t.Year}{(t.Found ? " found" : "")}{(t.Installed ? " installed" : "")}")));
        var log = session["MsiLogFileLocation"];
        _thread = new Thread(() => Run(info, log)) { Name = "ToadTools installer window" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (WaitHandle.WaitAny(new WaitHandle[] { _started, _closed }) == 1)
        {
            Trace.Write("Closed before the start");
            throw new InstallCanceledException();
        }

        Trace.Write("Start: " + _choice!.Action + " " + string.Join(",", _choice.Selected.Select(t => t.Feature)));
        Apply(session, info, _choice);
        internalUILevel = InstallUIOptions.Silent | InstallUIOptions.SourceResolutionOnly;
        return true;
    }

    public MessageResult ProcessMessage(InstallMessage messageType, Record messageRecord, MessageButtons buttons,
        MessageIcon icon, MessageDefaultButton defaultButton)
    {
        // The record is valid only during this call: read what is needed here, hand plain values to the window.
        var model = _model;
        var dispatcher = _dispatcher;
        if (model == null || dispatcher == null) return MessageResult.None;

        switch (messageType)
        {
            case InstallMessage.Progress:
                var fields = Enumerable.Range(1, 4).Select(i => Integer(messageRecord, i)).ToArray();
                dispatcher.BeginInvoke(new Action(() => model.OnProgress(fields, _counter)));
                break;
            case InstallMessage.ActionStart:
                var action = Text(messageRecord, 1);
                dispatcher.BeginInvoke(new Action(() => model.OnActionStart(action)));
                break;
            case InstallMessage.ActionData:
                var directory = messageRecord != null && messageRecord.FieldCount >= 9 ? Text(messageRecord, 9) : "";
                dispatcher.BeginInvoke(new Action(() => model.OnActionData(directory, _counter)));
                break;
            case InstallMessage.InstallEnd:
                var result = Integer(messageRecord, 3);
                dispatcher.BeginInvoke(new Action(() => model.OnEnd(result)));
                break;
            case InstallMessage.FilesInUse:
            case InstallMessage.RMFilesInUse:
                dispatcher.BeginInvoke(new Action(model.OnFilesInUse));
                // Replace the busy files after a restart instead of closing Revit behind the user's back.
                return MessageResult.Ignore;
            case InstallMessage.Error:
            case InstallMessage.FatalExit:
                var error = Format(messageRecord);
                dispatcher.BeginInvoke(new Action(() => model.OnError(error)));
                return Answer(buttons);
        }

        return model.CancelRequested ? MessageResult.Cancel : MessageResult.OK;
    }

    public void Shutdown()
    {
        var model = _model;
        _dispatcher?.BeginInvoke(new Action(() => model?.Finish()));
        _thread?.Join();
    }

    /// <summary>The window thread: theme, window, message loop until the window closes.</summary>
    private void Run(SetupInfo info, string log)
    {
        try
        {
            ToadTheme.Mode = RememberedTheme();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _model = new SetupViewModel(info, log);
            _model.Started += (_, choice) =>
            {
                _choice = choice;
                _started.Set();
            };

            _dispatcher.UnhandledException += (_, e) =>
            {
                Trace.Write("Window error: " + e.Exception);
                e.Handled = true;
            };

            var window = new InstallerWindow(_model);
            window.Closed += (_, _) => _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            window.Show();
            Trace.Write("Window shown, theme " + ToadTheme.ActualMode + ", process " + System.Diagnostics.Process.GetCurrentProcess().Id + " " + Environment.CommandLine);
            if (info.Mode == SetupMode.Remove) _model.Start(SetupAction.Remove);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            Trace.Write("Window failed: " + exception);
        }
        finally
        {
            _closed.Set();
        }
    }

    /// <summary>Puts the user's choice into the MSI properties that select features.</summary>
    private static void Apply(Session session, SetupInfo info, SetupChoice choice)
    {
        var selected = choice.Selected.Select(t => t.Feature).ToList();
        switch (choice.Action)
        {
            case SetupAction.Install:
                session["ADDLOCAL"] = string.Join(",", selected);
                break;
            case SetupAction.Change:
                var add = info.Targets.Where(t => !t.Installed && selected.Contains(t.Feature)).Select(t => t.Feature).ToList();
                var remove = info.Targets.Where(t => t.Installed && !selected.Contains(t.Feature)).Select(t => t.Feature).ToList();
                if (selected.Count == 0)
                {
                    session["REMOVE"] = "ALL";
                    break;
                }

                if (add.Count > 0) session["ADDLOCAL"] = string.Join(",", add);
                if (remove.Count > 0) session["REMOVE"] = string.Join(",", remove);
                break;
            case SetupAction.Repair:
                session["REINSTALL"] = "ALL";
                session["REINSTALLMODE"] = "amus";
                break;
            case SetupAction.Remove:
                session["REMOVE"] = "ALL";
                break;
        }
    }

    /// <summary>The theme chosen in the add-ins (%AppData%\ToadTools\theme.json), otherwise the Windows one.</summary>
    private static ToadThemeMode RememberedTheme()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "theme.json");
            if (File.Exists(path) && System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"""dark""\s*:\s*(true|false)") is { Success: true } match)
                return match.Groups[1].Value == "true" ? ToadThemeMode.Dark : ToadThemeMode.Light;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unreadable file: follow Windows.
        }

        return ToadThemeMode.System;
    }

    /// <summary>The answer to an error message box: give up rather than ask; the error page explains.</summary>
    private static MessageResult Answer(MessageButtons buttons) => buttons switch
    {
        MessageButtons.OK => MessageResult.OK,
        MessageButtons.AbortRetryIgnore => MessageResult.Abort,
        MessageButtons.YesNo => MessageResult.No,
        _ => MessageResult.Cancel
    };

    private static int Integer(Record? record, int field)
    {
        if (record == null || record.FieldCount < field || record.IsNull(field)) return 0;
        return record.GetInteger(field);
    }

    private static string Text(Record? record, int field) =>
        record == null || record.FieldCount < field ? "" : record.GetString(field) ?? "";

    private static string Format(Record? record)
    {
        if (record == null) return "";
        try
        {
            var text = record.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        catch (InstallerException)
        {
            // Fall back to the raw fields below.
        }

        return string.Join(" ", Enumerable.Range(1, record.FieldCount).Select(i => record.GetString(i)).Where(s => !string.IsNullOrEmpty(s)));
    }
}
