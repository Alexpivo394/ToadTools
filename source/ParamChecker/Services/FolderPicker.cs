using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ParamChecker.Services;

public static class FolderPicker
{
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    public static string? PickFolder(
        string? initialDirectory = null,
        string? title = null,
        IntPtr owner = default)
    {
        IFileDialog? dialog = null;
        IShellItem? initialFolder = null;
        IShellItem? result = null;

        try
        {
            dialog = (IFileDialog)new FileOpenDialog();

            dialog.GetOptions(out var options);

            options |= FileOpenOptions.PickFolders;
            options |= FileOpenOptions.ForceFileSystem;
            options |= FileOpenOptions.PathMustExist;

            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(title))
            {
                dialog.SetTitle(title!);
            }

            if (!string.IsNullOrWhiteSpace(initialDirectory) &&
                Directory.Exists(initialDirectory))
            {
                initialFolder = CreateShellItem(initialDirectory!);

                // Именно DefaultFolder, а не SetFolder:
                // Windows сможет учитывать последнюю использованную папку.
                dialog.SetDefaultFolder(initialFolder);
            }

            var resultCode = dialog.Show(owner);

            if (resultCode == ErrorCancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(resultCode);

            dialog.GetResult(out result);

            result.GetDisplayName(
                ShellItemDisplayName.FileSystemPath,
                out var pathPointer);

            try
            {
                return Marshal.PtrToStringUni(pathPointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
        finally
        {
            if (result != null)
                Marshal.ReleaseComObject(result);

            if (initialFolder != null)
                Marshal.ReleaseComObject(initialFolder);

            if (dialog != null)
                Marshal.ReleaseComObject(dialog);
        }
    }

    private static IShellItem CreateShellItem(string path)
    {
        var shellItemGuid = typeof(IShellItem).GUID;

        SHCreateItemFromParsingName(
            path,
            IntPtr.Zero,
            ref shellItemGuid,
            out var shellItem);

        return shellItem;
    }

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindingContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(
            uint count,
            IntPtr filterSpec);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(
            IntPtr events,
            out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(FileOpenOptions options);

        void GetOptions(out FileOpenOptions options);

        void SetDefaultFolder(IShellItem shellItem);

        void SetFolder(IShellItem shellItem);

        void GetFolder(out IShellItem shellItem);

        void GetCurrentSelection(out IShellItem shellItem);

        void SetFileName(
            [MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName(out IntPtr name);

        void SetTitle(
            [MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel(
            [MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel(
            [MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem shellItem);

        void AddPlace(
            IShellItem shellItem,
            FileDialogAddPlace location);

        void SetDefaultExtension(
            [MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int result);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(
            IntPtr bindingContext,
            ref Guid handler,
            ref Guid riid,
            out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(
            ShellItemDisplayName displayName,
            out IntPtr name);

        void GetAttributes(
            uint mask,
            out uint attributes);

        void Compare(
            IShellItem shellItem,
            uint hint,
            out int order);
    }

    [Flags]
    private enum FileOpenOptions : uint
    {
        OverwritePrompt = 0x00000002,
        StrictFileTypes = 0x00000004,
        NoChangeDirectory = 0x00000008,

        // Вот этот флаг превращает OpenDialog
        // в выбор папки
        PickFolders = 0x00000020,

        ForceFileSystem = 0x00000040,
        AllNonStorageItems = 0x00000080,
        NoValidate = 0x00000100,
        AllowMultiSelect = 0x00000200,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000,
        CreatePrompt = 0x00002000,
        ShareAware = 0x00004000,
        NoReadOnlyReturn = 0x00008000,
        NoTestFileCreate = 0x00010000,
        HideMruPlaces = 0x00020000,
        HidePinnedPlaces = 0x00040000,
        NoDereferenceLinks = 0x00100000,
        OkButtonNeedsInteraction = 0x00200000,
        DontAddToRecent = 0x02000000,
        ForceShowHidden = 0x10000000
    }

    private enum ShellItemDisplayName : uint
    {
        NormalDisplay = 0x00000000,

        // Нужен именно настоящий filesystem path:
        // C:\Users\Sanya\Desktop\Reports
        FileSystemPath = 0x80058000
    }

    private enum FileDialogAddPlace
    {
        Bottom = 0,
        Top = 1
    }
}