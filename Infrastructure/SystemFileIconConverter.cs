using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanShare.Models;

namespace LanShare.Infrastructure;

public sealed class SystemFileIconConverter : IValueConverter
{
    private static readonly ImageSource EmptyImageSource = CreateEmptyImageSource();
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            BrowseEntry browseEntry => ConvertEntry(browseEntry.IsDirectory, browseEntry.Name),
            SharedPathItem sharedPathItem => ConvertEntry(sharedPathItem.IsDirectory, sharedPathItem.DisplayPath),
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static ImageSource ConvertEntry(bool isDirectory, string name)
    {
        var cacheKey = isDirectory ? "__dir__" : GetExtensionKey(name);
        return Cache.GetOrAdd(cacheKey, _ => CreateImageSource(isDirectory, name) ?? EmptyImageSource);
    }

    private static string GetExtensionKey(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) ? "__file__" : extension;
    }

    private static ImageSource? CreateImageSource(bool isDirectory, string name)
    {
        var icon = GetShellIcon(isDirectory, name);
        if (icon.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.hIcon,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));

            imageSource.Freeze();
            return imageSource;
        }
        finally
        {
            DestroyIcon(icon.hIcon);
        }
    }

    private static ImageSource CreateEmptyImageSource()
    {
        var imageSource = new DrawingImage();
        imageSource.Freeze();
        return imageSource;
    }

    private static SHFILEINFO GetShellIcon(bool isDirectory, string name)
    {
        const uint fileFlags = ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes;
        const uint directoryAttributes = 0x10;
        const uint fileAttributes = 0x80;

        var path = isDirectory
            ? "folder"
            : $"dummy{GetExtensionKey(name)}";

        SHGetFileInfo(
            path,
            isDirectory ? directoryAttributes : fileAttributes,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            fileFlags);

        return fileInfo;
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("User32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
