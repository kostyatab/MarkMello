using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Пустой системный тулбар окна в стиле unified. Светофор macOS ставит по стилю тулбара:
/// без него кнопки прижаты к верхнему углу, с ним стоят по центру полосы высотой 52 —
/// там же, где у Finder, Почты и Заметок. Кнопки при этом остаются системными.
///
/// Тулбар ставится после включения расширенной клиентской области: включая её,
/// Avalonia снимает тулбар окна. Клики по его полосе всё равно получает содержимое окна.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOSWindowToolbar
{
    private const nint NSWindowToolbarStyleUnified = 3;
    private const nint NSTitlebarSeparatorStyleNone = 1;

    public static void Attach(TopLevel topLevel)
    {
        if (topLevel.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSWindow: var window }
            || window == IntPtr.Zero
            || objc_msgSend_retIntPtr(window, sel_registerName("toolbar")) != IntPtr.Zero)
        {
            return;
        }

        var identifier = CreateNsString("MarkMelloWindowRow");
        var toolbar = objc_msgSend_retIntPtr_arg(
            objc_msgSend_retIntPtr(objc_getClass("NSToolbar"), sel_registerName("alloc")),
            sel_registerName("initWithIdentifier:"),
            identifier);
        Release(identifier);

        // Окно удерживает тулбар само, своя ссылка после alloc/init больше не нужна.
        objc_msgSend_setIntPtr(window, sel_registerName("setToolbar:"), toolbar);
        Release(toolbar);

        objc_msgSend_setNInt(window, sel_registerName("setToolbarStyle:"), NSWindowToolbarStyleUnified);
        objc_msgSend_setNInt(window, sel_registerName("setTitlebarSeparatorStyle:"), NSTitlebarSeparatorStyleNone);
    }

    private static IntPtr CreateNsString(string value)
    {
        var nsString = objc_msgSend_retIntPtr(objc_getClass("NSString"), sel_registerName("alloc"));
        return objc_msgSend_utf8(
            nsString,
            sel_registerName("initWithUTF8String:"),
            Encoding.UTF8.GetBytes(value + '\0'));
    }

    private static void Release(IntPtr instance)
        => objc_msgSend_retIntPtr(instance, sel_registerName("release"));

    // ObjC runtime принимает имена классов и селекторов как ASCII-строки.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_retIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_retIntPtr_arg(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_utf8(IntPtr receiver, IntPtr selector, byte[] utf8);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setIntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setNInt(IntPtr receiver, IntPtr selector, nint value);
}
