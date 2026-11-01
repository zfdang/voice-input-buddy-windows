using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.Core.Protocols;

public static class G9N9NProtocol
{
    public const ushort VendorId = 0x18D1;
    public const ushort ProductId = 0x9450;

    public static readonly Dictionary<ushort, RemoteButton> ConsumerButtons = new()
    {
        [0x0042] = RemoteButton.Up,
        [0x0043] = RemoteButton.Down,
        [0x0044] = RemoteButton.Left,
        [0x0045] = RemoteButton.Right,
        [0x0041] = RemoteButton.Ok,
        [0x0224] = RemoteButton.Back,
        [0x0223] = RemoteButton.Home,
        [0x00E9] = RemoteButton.VolumeUp,
        [0x00EA] = RemoteButton.VolumeDown,
        [0x00E2] = RemoteButton.Mute,
        [0x00CD] = RemoteButton.PlayPause,
        [0x0077] = RemoteButton.YouTube,
        [0x0078] = RemoteButton.Netflix,
        [0x0189] = RemoteButton.Input,
        [0x0089] = RemoteButton.Input,
        [0x019E] = RemoteButton.Power,
        [0x0221] = RemoteButton.Menu
    };

    public static readonly ushort[] IndexedConsumerUsages =
    [
        0x019E, // 1: Power
        0x00CD, // 2: Play/Pause
        0x0042, // 3: Up
        0x0043, // 4: Down
        0x0044, // 5: Left
        0x0045, // 6: Right
        0x0041, // 7: Ok
        0x00E2, // 8: Mute
        0x0221, // 9: App
        0x0223, // 10: Home
        0x0224, // 11: Back
        0x00E9, // 12: VolUp
        0x00EA, // 13: VolDown
        0x0077, // 14: YouTube
        0x0078, // 15: Netflix
        0x0079, // 16: Custom
        0x0089  // 17: Input
    ];

    public static bool IsCandidate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string clean = name.Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
        return clean.Contains("chromecast") || clean.Contains("g9n9n");
    }

    public static bool TryParseReport1(ReadOnlySpan<byte> data, out List<RemoteButton> buttons)
    {
        buttons = new();
        if (data.IsEmpty) return false;

        ReadOnlySpan<byte> slice = data;
        if (slice.Length >= 3 && slice[0] == 1)
        {
            slice = slice[1..];
        }

        if (slice.Length < 2) return false;

        for (int i = 0; i < 2; i++)
        {
            byte index = slice[i];
            if (index > 0 && index <= IndexedConsumerUsages.Length)
            {
                ushort usage = IndexedConsumerUsages[index - 1];
                if (ConsumerButtons.TryGetValue(usage, out var btn))
                {
                    buttons.Add(btn);
                }
            }
        }

        return true;
    }

    public static bool TryParseReport2(ReadOnlySpan<byte> data, out List<RemoteButton> buttons)
    {
        buttons = new();
        if (data.IsEmpty) return false;

        ReadOnlySpan<byte> slice = data;
        if (slice.Length >= 3 && slice[0] == 2)
        {
            slice = slice[1..];
        }

        if (slice.Length < 2) return false;

        int usableLength = slice.Length - (slice.Length % 2);
        for (int i = 0; i < usableLength; i += 2)
        {
            ushort usage = (ushort)(slice[i] | (slice[i + 1] << 8));
            if (usage != 0 && ConsumerButtons.TryGetValue(usage, out var btn))
            {
                buttons.Add(btn);
            }
        }

        return true;
    }

    public static bool TryParseReport(ReadOnlySpan<byte> data, out List<RemoteButton> buttons)
    {
        if (data.Length >= 3 && data[0] == 1)
        {
            return TryParseReport1(data, out buttons);
        }
        if (data.Length >= 3 && data[0] == 2)
        {
            return TryParseReport2(data, out buttons);
        }

        // Fallback checks when driver omits the Report ID byte
        if (data.Length == 2)
        {
            if (TryParseReport1(data, out buttons) && buttons.Count > 0) return true;
        }
        if (data.Length >= 2 && data.Length % 2 == 0)
        {
            if (TryParseReport2(data, out buttons) && buttons.Count > 0) return true;
        }

        buttons = new();
        return false;
    }
}

public static class XiaomiProtocol
{
    public static bool IsCandidate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string clean = name.Trim().ToLowerInvariant();
        return clean.Contains("xiaomi") || clean.Contains("rc001") || clean.Contains("rc003") || clean.Contains("mi ");
    }

    public static RemoteModelType DetectModelType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return RemoteModelType.Unknown;
        string clean = name.Trim().ToLowerInvariant();
        if (clean.Contains("rc003")) return RemoteModelType.XiaomiRC003;
        if (clean.Contains("rc001")) return RemoteModelType.XiaomiRC001;
        if (G9N9NProtocol.IsCandidate(name)) return RemoteModelType.GoogleG9N9N;
        return RemoteModelType.Unknown;
    }
}
