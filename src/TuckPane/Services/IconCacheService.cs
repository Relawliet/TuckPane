using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TuckPane.Services;

public sealed class IconCacheService
{
    private const int JumboSize = 256;
    private const int FallbackSize = 32;
    private const string CacheVersion = "v3-url-icon";
    private readonly Dictionary<string, BitmapImage> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    internal static IntPtr CreateDragBitmap(string path, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        IntPtr icon = GetPreferredIcon(path, size, out _);
        try
        {
            byte[] pixels = DrawIconPixels(icon, size);
            UnpremultiplyAlpha(pixels);
            return CreateBitmap(pixels, size);
        }
        finally
        {
            _ = NativeMethods.DestroyIcon(icon);
        }
    }

    public async Task<BitmapImage?> GetIconAsync(string path, bool refresh = false)
    {
        AppPaths.EnsureCreated();
        string key = Path.GetFullPath(path);
        if (refresh) _memoryCache.Remove(key);
        if (!refresh && _memoryCache.TryGetValue(key, out BitmapImage? cached)) return cached;

        string cachePath = Path.Combine(AppPaths.IconCacheRoot, $"{Hash(key)}.png");
        if (refresh || !File.Exists(cachePath))
        {
            try
            {
                await RefreshAsync(key, cachePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Shell 图标提取失败：{key}", ex);
            }
        }

        if (!File.Exists(cachePath)) return null;
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(cachePath);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            _memoryCache[key] = image;
            return image;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"图标缓存读取失败：{cachePath}", ex);
            return null;
        }
    }

    private static async Task RefreshAsync(string path, string cachePath)
    {
        IconSnapshot snapshot = await Task.Run(() => ExtractShellIconPixels(path));
        StorageFolder cacheFolder = await StorageFolder.GetFolderFromPathAsync(AppPaths.IconCacheRoot);
        string temporaryName = $"{Path.GetFileNameWithoutExtension(cachePath)}.{Guid.NewGuid():N}.tmp";
        StorageFile temporary = await cacheFolder.CreateFileAsync(temporaryName, CreationCollisionOption.FailIfExists);
        try
        {
            using IRandomAccessStream output = await temporary.OpenAsync(FileAccessMode.ReadWrite);
            using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                snapshot.Pixels.AsBuffer(),
                BitmapPixelFormat.Bgra8,
                snapshot.Size,
                snapshot.Size,
                BitmapAlphaMode.Premultiplied);
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            File.Move(temporary.Path, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary.Path)) File.Delete(temporary.Path);
        }
    }

    private static IconSnapshot ExtractShellIconPixels(string path)
    {
        IntPtr icon = GetPreferredIcon(path, JumboSize, out int sourceSize);
        try
        {
            return new(DrawIconPixels(icon, sourceSize), sourceSize);
        }
        finally
        {
            _ = NativeMethods.DestroyIcon(icon);
        }
    }

    private static IntPtr GetPreferredIcon(string path, int requestedSize, out int sourceSize)
    {
        if (TryGetInternetShortcutIcon(path, requestedSize, out IntPtr internetShortcutIcon))
        {
            sourceSize = requestedSize;
            return internetShortcutIcon;
        }
        if (TryGetJumboIcon(path, out IntPtr jumboIcon))
        {
            sourceSize = JumboSize;
            return jumboIcon;
        }
        sourceSize = FallbackSize;
        return GetFallbackIcon(path);
    }

    private static bool TryGetInternetShortcutIcon(string path, int size, out IntPtr icon)
    {
        icon = IntPtr.Zero;
        if (!Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
        try
        {
            string iconFile = ReadInternetShortcutValue(path, "IconFile");
            if (string.IsNullOrWhiteSpace(iconFile)) return false;
            iconFile = Environment.ExpandEnvironmentVariables(iconFile.Trim());
            if (!Path.IsPathFullyQualified(iconFile))
                iconFile = Path.Combine(Path.GetDirectoryName(path)!, iconFile);
            iconFile = Path.GetFullPath(iconFile);
            if (iconFile.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(iconFile)) return false;

            int iconIndex = int.TryParse(
                ReadInternetShortcutValue(path, "IconIndex"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedIndex)
                ? parsedIndex
                : 0;
            int result = NativeMethods.SHDefExtractIcon(iconFile, iconIndex, 0, out icon, out IntPtr smallIcon, (uint)size);
            if (smallIcon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(smallIcon);
            if (result == 0 && icon != IntPtr.Zero) return true;
            if (icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(icon);
            icon = IntPtr.Zero;
        }
        catch
        {
            icon = IntPtr.Zero;
        }
        return false;
    }

    private static string ReadInternetShortcutValue(string path, string key)
    {
        var value = new StringBuilder(32768);
        _ = NativeMethods.GetPrivateProfileString("InternetShortcut", key, string.Empty, value, (uint)value.Capacity, path);
        return value.ToString();
    }

    private static bool TryGetJumboIcon(string path, out IntPtr icon)
    {
        icon = IntPtr.Zero;
        var shellInfo = new NativeMethods.SHFILEINFO { DisplayName = string.Empty, TypeName = string.Empty };
        UIntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref shellInfo,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SYSICONINDEX | NativeMethods.SHGFI_ADDOVERLAYS | NativeMethods.SHGFI_OVERLAYINDEX);
        if (result == UIntPtr.Zero) return false;

        if (shellInfo.Icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(shellInfo.Icon);

        Guid interfaceId = typeof(NativeMethods.IImageList).GUID;
        int hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref interfaceId, out NativeMethods.IImageList imageList);
        if (hr < 0) return false;
        try
        {
            int imageIndex = shellInfo.IconIndex & 0x00FFFFFF;
            int overlayIndex = (shellInfo.IconIndex >> 24) & 0xFF;
            uint flags = NativeMethods.ILD_TRANSPARENT | ((uint)overlayIndex << 8);
            return imageList.GetIcon(imageIndex, flags, out icon) >= 0 && icon != IntPtr.Zero;
        }
        finally
        {
            if (Marshal.IsComObject(imageList)) _ = Marshal.FinalReleaseComObject(imageList);
        }
    }

    private static IntPtr GetFallbackIcon(string path)
    {
        var shellInfo = new NativeMethods.SHFILEINFO { DisplayName = string.Empty, TypeName = string.Empty };
        UIntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref shellInfo,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | NativeMethods.SHGFI_ADDOVERLAYS);
        if (result != UIntPtr.Zero && shellInfo.Icon != IntPtr.Zero) return shellInfo.Icon;

        // 兜底：目标图标提取失败（断链快捷方式、目标无图标资源、权限受限等）时，
        // 退回系统默认图标，避免返回 null 导致收纳盒显示白板。
        if (shellInfo.Icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(shellInfo.Icon);
        string systemIconSource = Path.Combine(Environment.SystemDirectory, "shell32.dll");
        result = NativeMethods.SHGetFileInfo(
            systemIconSource,
            0,
            ref shellInfo,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | NativeMethods.SHGFI_ADDOVERLAYS);
        if (result == UIntPtr.Zero || shellInfo.Icon == IntPtr.Zero)
        {
            if (shellInfo.Icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(shellInfo.Icon);
            throw new InvalidOperationException($"Windows Shell 未返回图标：{path}");
        }
        return shellInfo.Icon;
    }

    private static byte[] DrawIconPixels(IntPtr icon, int size)
    {
        // 优先直接从图标的彩色位图读取真实颜色。DrawIconEx 对调色板型/掩码型图标
        // 只会绘制单色掩码，导致图标变成白板或灰块（例如部分程序的快捷方式）。
        byte[]? extracted = ExtractColorPixels(icon, size);
        if (extracted is not null)
        {
            RepairMissingAlpha(extracted);
            return extracted;
        }

        var bitmapInfo = new NativeMethods.BITMAPINFO
        {
            Header = new NativeMethods.BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BI_RGB,
                SizeImage = (uint)(size * size * 4)
            }
        };
        IntPtr dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        IntPtr bitmap = NativeMethods.CreateDIBSection(dc, ref bitmapInfo, NativeMethods.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previous = IntPtr.Zero;
        try
        {
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建图标缓冲区。");
            }
            previous = NativeMethods.SelectObject(dc, bitmap);
            byte[] pixels = new byte[size * size * 4];
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            if (!NativeMethods.DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL))
            {
                throw new InvalidOperationException("无法绘制 Shell 图标。");
            }
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            RepairMissingAlpha(pixels);
            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero && dc != IntPtr.Zero) _ = NativeMethods.SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) _ = NativeMethods.DeleteObject(bitmap);
            if (dc != IntPtr.Zero) _ = NativeMethods.DeleteDC(dc);
        }
    }

    private static byte[]? ExtractColorPixels(IntPtr icon, int size)
    {
        if (!NativeMethods.GetIconInfo(icon, out NativeMethods.ICONINFO iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return null;
            byte[]? pixels = ReadDibPixels(iconInfo.hbmColor, iconInfo.hbmMask, out int sourceWidth, out int sourceHeight);
            if (pixels is null) return null;
            return ResizePixels(pixels, sourceWidth, sourceHeight, size);
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) _ = NativeMethods.DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) _ = NativeMethods.DeleteObject(iconInfo.hbmMask);
        }
    }

    private static byte[]? ReadDibPixels(IntPtr hbm, IntPtr hbmMask, out int sourceWidth, out int sourceHeight)
    {
        sourceWidth = 0;
        sourceHeight = 0;
        IntPtr dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            var bmi = new NativeMethods.BITMAPINFO
            {
                Header = new NativeMethods.BITMAPINFOHEADER { Size = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>() }
            };
            if (NativeMethods.GetDIBits(dc, hbm, 0, 0, null, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0) return null;

            int width = bmi.Header.Width;
            int height = bmi.Header.Height;
            ushort bitCount = bmi.Header.BitCount;
            int absHeight = Math.Abs(height);
            bool topDown = height < 0;
            if (width <= 0 || absHeight == 0) return null;
            sourceWidth = width;
            sourceHeight = absHeight;

            if (bitCount is 32 or 24)
            {
                int bytesPerPixel = bitCount == 32 ? 4 : 3;
                int stride = (width * bytesPerPixel + 3) / 4 * 4;
                byte[] raw = new byte[stride * absHeight];
                if (NativeMethods.GetDIBits(dc, hbm, 0, (uint)absHeight, raw, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0) return null;
                return ConvertRgbPixels(raw, width, absHeight, stride, bytesPerPixel, topDown);
            }

            if (bitCount <= 8)
            {
                return ReadPalettePixels(dc, hbm, hbmMask, width, absHeight, bitCount, topDown);
            }

            return null;
        }
        finally
        {
            _ = NativeMethods.DeleteDC(dc);
        }
    }

    private static byte[] ConvertRgbPixels(byte[] raw, int width, int rows, int stride, int bytesPerPixel, bool topDown)
    {
        byte[] dst = new byte[width * rows * 4];
        for (int y = 0; y < rows; y++)
        {
            int sourceRow = topDown ? y : (rows - 1 - y);
            int source = sourceRow * stride;
            int destination = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int pixel = source + x * bytesPerPixel;
                dst[destination + x * 4 + 0] = raw[pixel + 0];
                dst[destination + x * 4 + 1] = raw[pixel + 1];
                dst[destination + x * 4 + 2] = raw[pixel + 2];
                dst[destination + x * 4 + 3] = bytesPerPixel == 4 ? raw[pixel + 3] : (byte)255;
            }
        }
        return dst;
    }

    private static byte[]? ReadPalettePixels(IntPtr dc, IntPtr hbm, IntPtr hbmMask, int width, int absHeight, ushort bitCount, bool topDown)
    {
        int headerSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        int paletteEntries = Math.Min(1 << bitCount, 256);
        IntPtr buffer = Marshal.AllocHGlobal(headerSize + paletteEntries * 4);
        try
        {
            var header = new NativeMethods.BITMAPINFOHEADER { Size = (uint)headerSize };
            Marshal.StructureToPtr(header, buffer, false);
            if (NativeMethods.GetDIBitsBuffer(dc, hbm, 0, 0, null, buffer, NativeMethods.DIB_RGB_COLORS) == 0) return null;

            int bitsPerPixel = bitCount;
            int stride = (width * bitsPerPixel + 31) / 32 * 4;
            byte[] raw = new byte[stride * absHeight];
            if (NativeMethods.GetDIBitsBuffer(dc, hbm, 0, (uint)absHeight, raw, buffer, NativeMethods.DIB_RGB_COLORS) == 0) return null;

            byte[] palette = new byte[paletteEntries * 4];
            Marshal.Copy(buffer + headerSize, palette, 0, palette.Length);

            byte[]? mask = hbmMask == IntPtr.Zero ? null : ReadMaskPixels(dc, hbmMask, width, absHeight);

            byte[] dst = new byte[width * absHeight * 4];
            for (int y = 0; y < absHeight; y++)
            {
                int sourceRow = topDown ? y : (absHeight - 1 - y);
                int rowStart = sourceRow * stride;
                int destination = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int index = GetPaletteIndex(raw, rowStart, x, bitsPerPixel);
                    int paletteOffset = Math.Min(index, paletteEntries - 1) * 4;
                    dst[destination + x * 4 + 0] = palette[paletteOffset + 0];
                    dst[destination + x * 4 + 1] = palette[paletteOffset + 1];
                    dst[destination + x * 4 + 2] = palette[paletteOffset + 2];
                    dst[destination + x * 4 + 3] = (mask is not null && mask[y * width + x] != 0) ? (byte)0 : (byte)255;
                }
            }
            return dst;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetPaletteIndex(byte[] raw, int rowStart, int x, int bitsPerPixel)
    {
        return bitsPerPixel switch
        {
            8 => raw[rowStart + x],
            4 => (raw[rowStart + (x >> 1)] >> ((x & 1) == 0 ? 4 : 0)) & 0x0F,
            2 => (raw[rowStart + (x >> 2)] >> ((3 - (x & 3)) * 2)) & 0x03,
            1 => (raw[rowStart + (x >> 3)] >> (7 - (x & 7))) & 0x01,
            _ => 0
        };
    }

    private static byte[]? ReadMaskPixels(IntPtr dc, IntPtr hbmMask, int width, int absHeight)
    {
        var bmi = new NativeMethods.BITMAPINFO
        {
            Header = new NativeMethods.BITMAPINFOHEADER { Size = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>() }
        };
        if (NativeMethods.GetDIBits(dc, hbmMask, 0, 0, null, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0) return null;
        int maskWidth = bmi.Header.Width;
        int maskHeight = Math.Abs(bmi.Header.Height);
        bool topDown = bmi.Header.Height < 0;
        if (maskWidth <= 0 || maskHeight <= 0) return null;
        int stride = (maskWidth + 31) / 32 * 4;
        byte[] raw = new byte[stride * maskHeight];
        if (NativeMethods.GetDIBits(dc, hbmMask, 0, (uint)maskHeight, raw, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0) return null;

        int effectiveHeight = Math.Min(absHeight, maskHeight);
        byte[] mask = new byte[width * absHeight];
        for (int y = 0; y < effectiveHeight; y++)
        {
            int sourceRow = topDown ? y : (maskHeight - 1 - y);
            int rowStart = sourceRow * stride;
            int limit = Math.Min(width, maskWidth);
            for (int x = 0; x < limit; x++)
            {
                int bit = (raw[rowStart + (x >> 3)] >> (7 - (x & 7))) & 1;
                mask[y * width + x] = (byte)bit;
            }
        }
        return mask;
    }

    private static byte[] ResizePixels(byte[] source, int sourceWidth, int sourceHeight, int size)
    {
        if (sourceWidth == size && sourceHeight == size) return source;
        byte[] dst = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            float sourceY = (y + 0.5f) * sourceHeight / size - 0.5f;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, sourceHeight - 1);
            int y1 = Math.Clamp(y0 + 1, 0, sourceHeight - 1);
            float fractionY = sourceY - y0;
            for (int x = 0; x < size; x++)
            {
                float sourceX = (x + 0.5f) * sourceWidth / size - 0.5f;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, sourceWidth - 1);
                int x1 = Math.Clamp(x0 + 1, 0, sourceWidth - 1);
                float fractionX = sourceX - x0;
                int d = (y * size + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    float top = source[(y0 * sourceWidth + x0) * 4 + channel] * (1 - fractionX)
                              + source[(y0 * sourceWidth + x1) * 4 + channel] * fractionX;
                    float bottom = source[(y1 * sourceWidth + x0) * 4 + channel] * (1 - fractionX)
                                 + source[(y1 * sourceWidth + x1) * 4 + channel] * fractionX;
                    dst[d + channel] = (byte)Math.Clamp(top * (1 - fractionY) + bottom * fractionY, 0, 255);
                }
            }
        }
        return dst;
    }

    private static void RepairMissingAlpha(byte[] pixels)
    {
        bool hasAlpha = false;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                hasAlpha = true;
                break;
            }
        }
        if (hasAlpha) return;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0 || pixels[index + 1] != 0 || pixels[index + 2] != 0) pixels[index + 3] = 255;
        }
    }

    private static void UnpremultiplyAlpha(byte[] pixels)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            int alpha = pixels[index + 3];
            if (alpha is 0 or 255) continue;
            pixels[index] = (byte)Math.Min(255, pixels[index] * 255 / alpha);
            pixels[index + 1] = (byte)Math.Min(255, pixels[index + 1] * 255 / alpha);
            pixels[index + 2] = (byte)Math.Min(255, pixels[index + 2] * 255 / alpha);
        }
    }

    private static IntPtr CreateBitmap(byte[] pixels, int size)
    {
        var bitmapInfo = new NativeMethods.BITMAPINFO
        {
            Header = new NativeMethods.BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BI_RGB,
                SizeImage = (uint)pixels.Length
            }
        };
        IntPtr bitmap = NativeMethods.CreateDIBSection(IntPtr.Zero, ref bitmapInfo, NativeMethods.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) _ = NativeMethods.DeleteObject(bitmap);
            throw new InvalidOperationException("无法创建Shell拖动图像。");
        }
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return bitmap;
    }

    private static string Hash(string path)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{CacheVersion}|{path.ToUpperInvariant()}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record IconSnapshot(byte[] Pixels, int Size);
}
