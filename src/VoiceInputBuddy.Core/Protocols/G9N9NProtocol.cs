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
        [0x0077] = RemoteButton.YouTube,
        [0x0078] = RemoteButton.Netflix,
        [0x0189] = RemoteButton.Input,
        [0x0089] = RemoteButton.Input,
        [0x019E] = RemoteButton.Power
    };

    public static bool IsCandidate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string clean = name.Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
        return clean.Contains("chromecast") || clean.Contains("g9n9n");
    }

    public static bool TryParseReport2(ReadOnlySpan<byte> data, out List<RemoteButton> buttons)
    {
        buttons = new();
        if (data.IsEmpty) return false;

        ReadOnlySpan<byte> slice = data;
        if (slice.Length % 2 == 1 && slice[0] == 2)
        {
            slice = slice[1..];
        }

        if (slice.IsEmpty || slice.Length % 2 != 0) return false;

        for (int i = 0; i < slice.Length; i += 2)
        {
            ushort usage = (ushort)(slice[i] | (slice[i + 1] << 8));
            if (usage != 0 && ConsumerButtons.TryGetValue(usage, out var btn))
            {
                buttons.Add(btn);
            }
        }

        return buttons.Count > 0;
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
